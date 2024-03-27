#I @"tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO
open System.Text


open Fake
open Fake.DotNetCli
open Fake.DocFxHelper
open Fake.NuGet.Install

// Variables
let configuration = "Release"
let solution = System.IO.Path.GetFullPath(string "./src/Alpakka.sln")

// Directories
let toolsDir = __SOURCE_DIRECTORY__ @@ "tools"
let output = __SOURCE_DIRECTORY__  @@ "bin"
let outputTests = __SOURCE_DIRECTORY__ @@ "TestResults"
let outputPerfTests = __SOURCE_DIRECTORY__ @@ "PerfResults"
let outputBinaries = output @@ "binaries"
let outputNuGet = output @@ "nuget"
let outputBinariesNet45 = outputBinaries @@ "net461"
let outputBinariesNetStandard = outputBinaries @@ "netstandard2.0"

let buildNumber = environVarOrDefault "BUILD_NUMBER" "0"
let hasTeamCity = (not (buildNumber = "0")) // check if we have the TeamCity environment variable for build # set
let preReleaseVersionSuffix = "beta" + (if (not (buildNumber = "0")) then (buildNumber) else DateTime.UtcNow.Ticks.ToString())

let releaseNotes =
    File.ReadLines (__SOURCE_DIRECTORY__ @@ "RELEASE_NOTES.md")
    |> ReleaseNotesHelper.parseReleaseNotes

let versionFromReleaseNotes =
    match releaseNotes.SemVer.PreRelease with
    | Some r -> r.Origin
    | None -> ""

let versionSuffix = 
    match (getBuildParam "nugetprerelease") with
    | "dev" -> preReleaseVersionSuffix
    | "" -> versionFromReleaseNotes
    | str -> str
    

// Incremental builds
let runIncrementally = hasBuildParam "incremental"
let incrementalistReport = output @@ "incrementalist.txt"

// Configuration values for tests
let testNetFrameworkVersion = "net471"
let testNetVersion = "net6.0"

Target "Clean" (fun _ ->
    ActivateFinalTarget "KillCreatedProcesses"

    CleanDir output
    CleanDir outputTests
    CleanDir outputPerfTests
    CleanDir outputBinaries
    CleanDir outputNuGet
    CleanDir outputBinariesNet45
    CleanDir outputBinariesNetStandard
    CleanDir "docs/_site"

    CleanDirs !! "./**/bin"
    CleanDirs !! "./**/obj"
)

//--------------------------------------------------------------------------------
// Incrementalist targets 
//--------------------------------------------------------------------------------
// Pulls the set of all affected projects detected by Incrementalist from the cached file
let getAffectedProjectsTopology = 
    lazy(
        log (sprintf "Checking inside %s for changes" incrementalistReport)

        let incrementalistFoundChanges = File.Exists incrementalistReport

        log (sprintf "Found changes via Incrementalist? %b - searched inside %s" incrementalistFoundChanges incrementalistReport)
        if not incrementalistFoundChanges then None
        else
            let sortedItems = (File.ReadAllLines incrementalistReport) |> Seq.map (fun x -> (x.Split ','))
                              |> Seq.map (fun items -> (items.[0], items))
            let d = dict sortedItems
            Some(d)
    )

let getAffectedProjects = 
    lazy(
        let finalProjects = getAffectedProjectsTopology.Value
        match finalProjects with
        | None -> None
        | Some p -> Some (p.Values |> Seq.concat)
    )

Target "ComputeIncrementalChanges" (fun _ ->
    if runIncrementally then
        let targetBranch = match getBuildParam "targetBranch" with
                            | "" -> "dev"
                            | null -> "dev"
                            | b -> b
        let incrementalistPath =
                let incrementalistDir = toolsDir @@ "incrementalist"
                let globalTool = tryFindFileOnPath "incrementalist.exe"
                match globalTool with
                    | Some t -> t
                    | None -> if isWindows then findToolInSubPath "incrementalist.exe" incrementalistDir
                              elif isMacOS then incrementalistDir @@ "incrementalist" 
                              else incrementalistDir @@ "incrementalist" 
    
   
        let args = StringBuilder()
                |> append "-b"
                |> append targetBranch
                |> append "-s"
                |> append solution
                |> append "-f"
                |> append incrementalistReport
                |> toText

        let result = ExecProcess(fun info -> 
            info.FileName <- incrementalistPath
            info.WorkingDirectory <- __SOURCE_DIRECTORY__
            info.Arguments <- args) (System.TimeSpan.FromMinutes 5.0) (* Reasonably long-running task. *)
        
        if result <> 0 then failwithf "Incrementalist failed. %s" args  
    else
        log "Skipping Incrementalist - not enabled for this build"
)

let filterProjects selectedProject =
    if runIncrementally then
        let affectedProjects = getAffectedProjects.Value

        match affectedProjects with
        | None -> None
        | Some x when x |> Seq.exists (fun n -> n.Contains (System.IO.Path.GetFileName(string selectedProject))) -> Some selectedProject
        | _ -> None
    else
        log "Not running incrementally"
        Some selectedProject

//--------------------------------------------------------------------------------
// Build targets 
//--------------------------------------------------------------------------------
let skipBuild = 
    lazy(
        match getAffectedProjects.Value with
        | None when runIncrementally -> true
        | _ -> false
    )

let headProjects =
    lazy(
        match getAffectedProjectsTopology.Value with
        | None when runIncrementally -> [||]
        | None -> [|solution|]
        | Some p -> p.Keys |> Seq.toArray
    )

Target "AssemblyInfo" (fun _ ->
    XmlPokeInnerText "./src/Directory.Build.props" "//Project/PropertyGroup/VersionPrefix" releaseNotes.AssemblyVersion    
    XmlPokeInnerText "./src/Directory.Build.props" "//Project/PropertyGroup/PackageReleaseNotes" (releaseNotes.Notes |> String.concat "\n")
)

Target "Build" (fun _ ->   
    if not skipBuild.Value then
        let additionalArgs = if versionSuffix.Length > 0 then [sprintf "/p:VersionSuffix=%s" versionSuffix] else []  
        let buildProject proj =
            DotNetCli.Build
                (fun p -> 
                    { p with
                        Project = proj
                        Configuration = configuration
                        AdditionalArgs = additionalArgs })

        match getAffectedProjects.Value with
        | Some p -> p |> Seq.iter buildProject
        | None -> buildProject solution // build the entire solution if incrementalist is disabled
)

//--------------------------------------------------------------------------------
// Tests targets 
//--------------------------------------------------------------------------------
type Runtime =
    | NetCore
    | NetFramework

let getTestAssembly runtime project =
    let assemblyPath = match runtime with
                        | NetCore -> !! ("src" @@ "**" @@ "bin" @@ "Release" @@ testNetVersion @@ fileNameWithoutExt project + ".dll")
                        | NetFramework -> !! ("src" @@ "**" @@ "bin" @@ "Release" @@ testNetFrameworkVersion @@ fileNameWithoutExt project + ".dll")

    if Seq.isEmpty assemblyPath then
        None
    else
        Some (assemblyPath |> Seq.head)

module internal ResultHandling =
    let (|OK|Failure|) = function
        | 0 -> OK
        | x -> Failure x

    let buildErrorMessage = function
        | OK -> None
        | Failure errorCode ->
            Some (sprintf "xUnit2 reported an error (Error Code %d)" errorCode)

    let failBuildWithMessage = function
        | DontFailBuild -> traceError
        | _ -> (fun m -> raise(FailedTestsException m))

    let failBuildIfXUnitReportedError errorLevel =
        buildErrorMessage
        >> Option.iter (failBuildWithMessage errorLevel)

Target "RunTests" (fun _ ->    
    let projects = 
        let rawProjects = match (isWindows) with 
                            | true -> !! "./src/**/*.Tests.*sproj"
                                      -- "./src/**/*.SignalR.AspNetCore.Tests.csproj" // ASP.NET signalr spec does not support .NET FX
                                      -- "./src/**/*.RabbitMq.Tests.csproj" // Skip RabbitMq tests, no compatible windows docker image
                                      -- "./src/**/*.Amqp.V1.Tests.csproj" // Skip AMQP tests, no compatible windows docker image
                            | _ -> !! "./src/**/*.Tests.*sproj" // if you need to filter specs for Linux vs. Windows, do it here
                                   -- "./src/**/*.SignalR.AspNetCore.Tests.csproj" // ASP.NET signalr spec does not support .NET FX
        rawProjects |> Seq.choose filterProjects
    
    let runSingleProject project =
        let arguments =
            match (hasTeamCity) with
            | true -> (sprintf "test -c Release --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s --results-directory \"%s\" -- -parallel none -teamcity" testNetFrameworkVersion outputTests)
            | false -> (sprintf "test -c Release --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s --results-directory \"%s\" -- -parallel none" testNetFrameworkVersion outputTests)

        let result = ExecProcess(fun info ->
            info.FileName <- "dotnet"
            info.WorkingDirectory <- (Directory.GetParent project).FullName
            info.Arguments <- arguments) (TimeSpan.FromMinutes 60.0) // Need to bump this because pulling docker image takes a long time
        
        ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.DontFailBuild result

    CreateDir outputTests
    projects |> Seq.iter (runSingleProject)
)

Target "RunTestsNet" (fun _ ->
    if not skipBuild.Value then
        let projects = 
            let rawProjects = match (isWindows) with 
                                | true -> !! "./src/**/*.Tests.*sproj"
                                          -- "./src/**/*.RabbitMq.Tests.csproj" // Skip RabbitMq tests, no compatible windows docker image
                                          -- "./src/**/*.Amqp.V1.Tests.csproj" // Skip AMQP tests, no compatible windows docker image
                                | _ -> !! "./src/**/*.Tests.*sproj" // if you need to filter specs for Linux vs. Windows, do it here
            rawProjects |> Seq.choose filterProjects
     
        let runSingleProject project =
            let arguments =
                match (hasTeamCity) with
                | true -> (sprintf "test -c Release --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s --results-directory \"%s\" -- -parallel none -teamcity" testNetVersion outputTests)
                | false -> (sprintf "test -c Release --no-build --logger:trx --logger:\"console;verbosity=normal\" --framework %s --results-directory \"%s\" -- -parallel none" testNetVersion outputTests)

            let result = ExecProcess(fun info ->
                info.FileName <- "dotnet"
                info.WorkingDirectory <- (Directory.GetParent project).FullName
                info.Arguments <- arguments) (TimeSpan.FromMinutes 60.0) // Need to bump this because pulling docker image takes a long time
        
            ResultHandling.failBuildIfXUnitReportedError TestRunnerErrorLevel.DontFailBuild result

        CreateDir outputTests
        projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

Target "CreateNuget" (fun _ ->    
    CreateDir outputNuGet // need this to stop Azure pipelines copy stage from error-ing out
    if not skipBuild.Value then
        let projects = 
            let rawProjects = !! "src/**/*.*sproj"
                            -- "src/**/*.Tests*.*sproj"
                            -- "src/**/benchmark/**/*.*sproj"
                            -- "src/**/examples/**/*.*sproj"
            rawProjects |> Seq.choose filterProjects

        let runSingleProject project =
            DotNetCli.Pack
                (fun p -> 
                    { p with
                        Project = project
                        Configuration = configuration
                        AdditionalArgs = ["--include-symbols"]
                        VersionSuffix = versionSuffix
                        OutputPath = "\"" + outputNuGet + "\"" })

        projects |> Seq.iter (runSingleProject)
)

Target "PublishNuget" (fun _ ->
    let nugetExe = FullName @"./tools/nuget.exe"
    let rec publishPackage url apiKey trialsLeft packageFile =
        let tracing = enableProcessTracing
        enableProcessTracing <- false
        let args p =
            match p with
            | (pack, key, "") -> sprintf "push \"%s\" %s" pack key
            | (pack, key, url) -> sprintf "push \"%s\" %s -source %s" pack key url

        tracefn "Pushing %s Attempts left: %d" (FullName packageFile) trialsLeft
        try 
            DotNetCli.RunCommand
                (fun p -> 
                    { p with 
                        TimeOut = TimeSpan.FromMinutes 10. })
                (sprintf "nuget push %s --api-key %s --source %s" packageFile apiKey url)
        with exn -> 
            if (trialsLeft > 0) then (publishPackage url apiKey (trialsLeft-1) packageFile)
            else raise exn
    let shouldPushNugetPackages = hasBuildParam "nugetkey"
    
    if (shouldPushNugetPackages) then
        printfn "Pushing nuget packages"
        if shouldPushNugetPackages then
            let normalPackages= !! (outputNuGet @@ "*.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in normalPackages do
                try
                    publishPackage (getBuildParamOrDefault "nugetpublishurl" "https://api.nuget.org/v3/index.json") (getBuildParam "nugetkey") 3 package
                with exn ->
                    printfn "%s" exn.Message
)

//--------------------------------------------------------------------------------
// Documentation 
//--------------------------------------------------------------------------------  
Target "DocFx" (fun _ ->
    // build the projects with samples
    // let docsTestsProject = "./src/core/Akka.Docs.Tests/Akka.Docs.Tests.csproj"
    // DotNetCli.Restore (fun p -> { p with Project = docsTestsProject })
    // DotNetCli.Build (fun p -> { p with Project = docsTestsProject; Configuration = configuration })
    // let docsTutorialsProject = "./src/core/Akka.Docs.Tutorials/Akka.Docs.Tutorials.csproj"
    // DotNetCli.Restore (fun p -> { p with Project = docsTutorialsProject })
    // DotNetCli.Build (fun p -> { p with Project = docsTutorialsProject; Configuration = configuration })

    // install MSDN references
    NugetInstall (fun p -> 
            { p with
                ExcludeVersion = true
                Version = "0.1.0-alpha-1611021200"
                OutputDirectory = currentDirectory @@ "tools" }) "msdn.4.5.2"

    let docsPath = "./docs"
    DocFx (fun p -> 
                { p with 
                    Timeout = TimeSpan.FromMinutes 30.0; 
                    WorkingDirectory  = docsPath; 
                    DocFxJson = docsPath @@ "docfx.json" })
)

FinalTarget "KillCreatedProcesses" (fun _ ->
    log "Shutting down dotnet build-server"
    let result = ExecProcess(fun info -> 
            info.FileName <- "dotnet"
            info.WorkingDirectory <- __SOURCE_DIRECTORY__
            info.Arguments <- "build-server shutdown") (System.TimeSpan.FromMinutes 2.0)
    if result <> 0 then failwithf "dotnet build-server shutdown failed"
)

//--------------------------------------------------------------------------------
// Help 
//--------------------------------------------------------------------------------

Target "Help" <| fun _ ->
    List.iter printfn [
      "usage:"
      "/build [target]"
      ""
      " Targets for building:"
      " * Build      Builds"
      " * Nuget      Create and optionally publish nugets packages"
      " * RunTests   Runs tests"
      " * All        Builds, run tests, creates and optionally publish nuget packages"
      ""
      " Other Targets"
      " * Help       Display this help" 
      ""]

Target "HelpNuget" <| fun _ ->
    List.iter printfn [
      "usage: "
      "build Nuget [nugetkey=<key> [nugetpublishurl=<url>]] [symbolskey=<key> symbolspublishurl=<url>]"
      ""
      "In order to publish a nuget package, keys must be specified."
      "If a key is not specified the nuget packages will only be created on disk"
      "After a build you can find them in build/nuget"
      ""
      "For pushing nuget packages and symbols to nuget.org"
      "you need to specify nugetkey=<key>"
      "   build Nuget nugetKey=<key for nuget.org>"
      ""
      "For pushing the ordinary nuget packages to another place than nuget.org specify the url"
      "  nugetkey=<key>  nugetpublishurl=<url>  "
      ""
      "For pushing symbols packages specify:"
      "  symbolskey=<key>  symbolspublishurl=<url> "
      ""
      "Examples:"
      "  build Nuget                      Build nuget packages to the build/nuget folder"
      ""
      "  build Nuget versionsuffix=beta1  Build nuget packages with the custom version suffix"
      ""
      "  build Nuget nugetkey=123         Build and publish to nuget.org and symbolsource.org"
      ""
      "  build Nuget nugetprerelease=dev nugetkey=123 nugetpublishurl=http://abcsymbolspublishurl=http://xyz"
      ""]

//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "BuildRelease" DoNothing
Target "All" DoNothing
Target "Nuget" DoNothing
Target "RunTestsFull" DoNothing
Target "RunTestsNetCoreFull" DoNothing

// build dependencies
"Clean" ==> "AssemblyInfo" ==> "Build"
"Build" ==> "BuildRelease"
"ComputeIncrementalChanges" ==> "Build" // compute incremental changes

// tests dependencies
"Build" ==> "RunTests"
"Build" ==> "RunTestsNet"

// nuget dependencies
"BuildRelease" ==> "CreateNuget" ==> "PublishNuget" ==> "Nuget"

// docs
"BuildRelease" ==> "Docfx"

// all
"BuildRelease" ==> "All"
"RunTests" ==> "All"
"RunTestsNet" ==> "All"

RunTargetOrDefault "Help"