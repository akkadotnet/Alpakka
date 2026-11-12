# Code standards and build checks

Run `dotnet build Alpakka.slnx -c Release` with the repository SDK before submitting a change. The normal CI build runs the same compiler and SDK analyzers.

The required Code Standards jobs build the solution on Linux and Windows, then run `pwsh -File build-system/Verify-CodeStandards.ps1`. That check compiles a clean control and requires a deliberately invalid example of every enforced rule to fail with the intended compiler diagnostic. Temporary probe projects inherit the repository settings and are removed afterward; the examples are never executed or shipped.

The build rejects unobserved asynchronous calls reported as CS4014, and nullable warnings in code that has already opted into nullable analysis. It also enforces these SDK reliability rules:

| Rule | Required behavior |
| --- | --- |
| [CA2012](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2012) | Consume a `ValueTask` once, normally by awaiting it. |
| [CA2014](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2014) | Allocate stack buffers outside loops. |
| [CA2200](https://learn.microsoft.com/dotnet/fundamentals/code-analysis/quality-rules/ca2200) | Preserve exception stacks when rethrowing from a catch handler. |

These checks apply to the existing projects and new code. Enable nullable analysis for new internal and test code. Review public nullable annotations as API changes: existing library annotations, signatures, target frameworks, defaults and ownership contracts must stay compatible. The shared settings enforce opted-in nullable contexts; they do not enable nullable annotations across legacy libraries.

Fix a finding or explain a necessary exception during review. Do not add blanket warning suppressions, disable tests or weaken assertions to make a check pass. Existing warnings outside the enforced rules remain visible; this is an incremental policy, not a claim that every legacy warning has been resolved.

NuGet auditing is explicitly enabled for direct and transitive dependencies at every severity. Restore warnings remain visible. A warning alone is not a failing security gate: package-specific vulnerability checks must enforce their stated scope, including the exact packaged consumers where applicable. When a driver patch is incompatible, retain the evidence and resolve the compatibility decision before raising its minimum version; do not hide the advisory.

Changes to public APIs or serialization require compatibility evidence. Prefer small regression cases for observed behavior, preserve useful existing tests, and use Aspire.Hosting.Testing or Testcontainers for service boundaries. Keep implementation redesign separate from dependency, testing and documentation maintenance.
