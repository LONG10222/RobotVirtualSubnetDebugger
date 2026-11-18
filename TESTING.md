# Build and safe tests

Requires official .NET 8 SDK. PowerShell 7 is needed only for script mock tests.

- `dotnet build RobotVirtualSubnetDebugger/RobotVirtualSubnetDebugger.csproj -p:EnableWindowsTargeting=true`
- `(cd tests && dotnet run)` generates script fixtures and runs production plan, persistence and operation-lock checks without executing OS network changes.
- `(cd tests && pwsh -NoProfile -File nat-mocks.ps1)`
- `(cd tests && pwsh -NoProfile -File export-mock-run.ps1)`
- `(cd tests && pwsh -NoProfile -File export-lock-tests.ps1)`
- `(cd proxy-tests && dotnet run -- half)` opens loopback TCP sockets for protocol/resource and half-close tests. It does not control a real robot.

Additional authorization and replay-cache tests:

- `dotnet run --project tests/Nonce/Nonce.csproj`
- `dotnet run --project tests/Target/Target.csproj`

The replay cache retains authenticated nonces for ten minutes and rejects new messages at its bounded capacity (131072 per service instance). High-frequency or sustained traffic can reach this limit; this is a fail-closed protection, not an unlimited-throughput guarantee.

The operation lock prevents concurrent operations by the same OS user using this version of the app or its generated scripts. It is not a global system/network lock, does not protect against old scripts, other users or external tools, and does not implement authorization filtering of forwarded traffic.

A successful Windows-targeted build on Linux is not Windows runtime or real-robot acceptance. Actual routing/NAT, source-IP binding, broadcasts, callbacks and protocol compatibility remain unverified without an isolated Windows topology and the real SDK requirements.
