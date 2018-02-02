#I @"tools/FAKE/tools"
#r "FakeLib.dll"

open System
open System.IO
open System.Text

open Fake
open Fake.DotNetCli
open Fake.DocFxHelper

// Variables
let configuration = "Release"

// Directories
let output = __SOURCE_DIRECTORY__  @@ "build"
let outputTests = output @@ "tests"
let outputBinaries = output @@ "binaries"
let outputNuGet = output @@ "nuget"

let buildNumber = environVarOrDefault "BUILD_NUMBER" "0"
let preReleaseVersionSuffix = "beta" + (if (not (buildNumber = "0")) then (buildNumber) else "")
let versionSuffix = 
    match (getBuildParam "nugetprerelease") with
    | "dev" -> preReleaseVersionSuffix
    | _ -> ""

Target "Clean" (fun _ ->
    CleanDir output
    CleanDir outputTests
    CleanDir outputBinaries
    CleanDir outputNuGet

    CleanDirs !! "./**/bin"
    CleanDirs !! "./**/obj"
)

Target "RestorePackages" (fun _ ->
    let projects = !! "./**/Akka.Streams.Xml.csproj"
                   ++ "./**/Akka.Streams.Xml.Tests.csproj"
                   ++ "./**/Akka.Streams.Csv.csproj"
                   ++ "./**/Akka.Streams.Csv.Tests.csproj"
                   ++ "./**/Akka.Streams.SignalR.csproj"
                   ++ "./**/Akka.Streams.SignalR.Tests.csproj"
                   ++ "./**/Akka.Streams.Azure.EventHub.csproj"
                   ++ "./**/Akka.Streams.Azure.ServiceBus.csproj"
                   ++ "./**/Akka.Streams.Azure.StorageQueue.csproj"
                   ++ "./**/Akka.Streams.Amqp.csproj"
                   ++ "./**/Akka.Streams.Amqp.Tests.csproj"

    let runSingleProject project =
        DotNetCli.Restore
            (fun p -> 
                { p with
                    Project = project
                    NoCache = false })

    projects |> Seq.iter (runSingleProject)
)

Target "Build" (fun _ ->
    let projects = !! "./**/Akka.Streams.Xml.csproj"
                   ++ "./**/Akka.Streams.Xml.Tests.csproj"
                   ++ "./**/Akka.Streams.Csv.csproj"
                   ++ "./**/Akka.Streams.Csv.Tests.csproj"
                   ++ "./**/Akka.Streams.SignalR.csproj"
                   ++ "./**/Akka.Streams.SignalR.Tests.csproj"
                   ++ "./**/Akka.Streams.Azure.EventHub.csproj"
                   ++ "./**/Akka.Streams.Azure.ServiceBus.csproj"
                   ++ "./**/Akka.Streams.Azure.StorageQueue.csproj"
                   ++ "./**/Akka.Streams.Amqp.csproj"
                   ++ "./**/Akka.Streams.Amqp.Tests.csproj"

    let runSingleProject project =
        DotNetCli.Build
                (fun p -> 
                    { p with
                        Project = project
                        Configuration = configuration })

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Tests targets 
//--------------------------------------------------------------------------------

Target "RunTests" (fun _ ->
    let projects = !! "./**/Akka.Streams.Xml.Tests.csproj"
                   ++ "./**/Akka.Streams.Csv.Tests.csproj"
                   //++ "./**/Akka.Streams.SignalR.Tests.csproj"
                   //++ "./**/Akka.Streams.Amqp.Tests.csproj"

    let runSingleProject project =
        DotNetCli.RunCommand
            (fun p -> 
                { p with 
                    WorkingDir = (Directory.GetParent project).FullName
                    TimeOut = TimeSpan.FromMinutes 10. })
                (sprintf "xunit -parallel none -teamcity -xml %s_xunit.xml" (outputTests @@ fileNameWithoutExt project)) 

    projects |> Seq.iter (runSingleProject)
)

//--------------------------------------------------------------------------------
// Nuget targets 
//--------------------------------------------------------------------------------

Target "CreateNuget" (fun _ ->
    let envBuildNumber = environVarOrDefault "APPVEYOR_BUILD_NUMBER" "0"
    let branch =  environVarOrDefault "APPVEYOR_REPO_BRANCH" ""
    let versionSuffix = if branch.Equals("dev") then (sprintf "beta%s" envBuildNumber) else ""

    let projects = !! "./**/Akka.Streams.Xml.csproj"
                   ++ "./**/Akka.Streams.Csv.csproj"
                   ++ "./**/Akka.Streams.SignalR.csproj"
                   ++ "./**/Akka.Streams.Azure.EventHub.csproj"
                   ++ "./**/Akka.Streams.Azure.ServiceBus.csproj"
                   ++ "./**/Akka.Streams.Azure.StorageQueue.csproj"
                   ++ "./**/Akka.Streams.Amqp.csproj"


    let runSingleProject project =
        DotNetCli.Pack
            (fun p -> 
                { p with
                    Project = project
                    Configuration = configuration
                    AdditionalArgs = ["--include-symbols"]
                    VersionSuffix = versionSuffix
                    OutputPath = outputNuGet })

    projects |> Seq.iter (runSingleProject)
)

Target "PublishNuget" (fun _ ->
    let nugetExe = FullName @"./tools/nuget.exe"
    let rec publishPackage url accessKey trialsLeft packageFile =
        let tracing = enableProcessTracing
        enableProcessTracing <- false
        let args p =
            match p with
            | (pack, key, "") -> sprintf "push \"%s\" %s" pack key
            | (pack, key, url) -> sprintf "push \"%s\" %s -source %s" pack key url

        tracefn "Pushing %s Attempts left: %d" (FullName packageFile) trialsLeft
        try 
            let result = ExecProcess (fun info -> 
                    info.FileName <- nugetExe
                    info.WorkingDirectory <- (Path.GetDirectoryName (FullName packageFile))
                    info.Arguments <- args (packageFile, accessKey,url)) (System.TimeSpan.FromMinutes 1.0)
            enableProcessTracing <- tracing
            if result <> 0 then failwithf "Error during NuGet symbol push. %s %s" nugetExe (args (packageFile, "key omitted",url))
        with exn -> 
            if (trialsLeft > 0) then (publishPackage url accessKey (trialsLeft-1) packageFile)
            else raise exn
    let shouldPushNugetPackages = hasBuildParam "nugetkey"
    let shouldPushSymbolsPackages = (hasBuildParam "symbolspublishurl") && (hasBuildParam "symbolskey")
    
    if (shouldPushNugetPackages || shouldPushSymbolsPackages) then
        printfn "Pushing nuget packages"
        if shouldPushNugetPackages then
            let normalPackages= 
                !! (outputNuGet @@ "*.nupkg") 
                -- (outputNuGet @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in normalPackages do
                try
                    publishPackage (getBuildParamOrDefault "nugetpublishurl" "") (getBuildParam "nugetkey") 3 package
                with exn ->
                    printfn "%s" exn.Message

        if shouldPushSymbolsPackages then
            let symbolPackages= !! (outputNuGet @@ "*.symbols.nupkg") |> Seq.sortBy(fun x -> x.ToLower())
            for package in symbolPackages do
                try
                    publishPackage (getBuildParam "symbolspublishurl") (getBuildParam "symbolskey") 3 package
                with exn ->
                    printfn "%s" exn.Message
)


//--------------------------------------------------------------------------------
//  Target dependencies
//--------------------------------------------------------------------------------

Target "BuildRelease" DoNothing
Target "All" DoNothing
Target "Nuget" DoNothing

// build dependencies
"Clean" ==> "RestorePackages" ==> "Build" ==> "BuildRelease"

// nuget dependencies
"Clean" ==> "RestorePackages" ==> "Build" ==> "CreateNuget" ==> "PublishNuget" ==> "Nuget"

// all
"BuildRelease" ==> "All"

RunTargetOrDefault "All"