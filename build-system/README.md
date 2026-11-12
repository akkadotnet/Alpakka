# Azure Pipelines Build Files
These `.yaml` files are used by Windows Azure DevOps Pipelines to help execute the following types of builds:

- Pull request validation on Linux (Mono / .NET Core)
- Pull request validation on Windows (.NET Framework / .NET Core)
- NuGet releases with automatic release notes posted to a Github Release repository.

**NOTE**: you will need to change some of the pipeline variables inside the `windows-release.yaml` for your specific project and you will also want to create variable groups with your signing and NuGet push information.

The Windows driver-upgrade check runs an unchanged, previously compiled .NET Framework consumer against the exact candidate NuGet archive. MSBuild generates the output binding redirects with the frozen executable included among its reference inputs, preserving non-binding host settings and every executable/runtime DLL hash. The required check rejects the expected SDK binding failure without that generated policy, requires success with it, then restores the original configuration and requires the same binding failure again. This verifies a normal NuGet upgrade with generated output configuration; it does not promise a DLL-only swap with unchanged generated configuration or compatibility with arbitrary custom host binding policies. Public API, retained TFM, package and native integration checks remain required.
