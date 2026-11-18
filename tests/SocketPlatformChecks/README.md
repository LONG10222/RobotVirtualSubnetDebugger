# Windows/macOS real-socket validation

This dependency-free .NET 8 console harness links the actual production proxy and model
source files. Only logging and port-preflight checks are substituted. Actual OS TCP
sockets, production framing, authentication, crypto, authorization, timeouts, and
half-close handling execute unchanged. Test keys are generated per process and not saved.

The harness never launches the WPF application, elevates, edits IP addresses/routes/NAT,
changes firewall/DNS/network settings, probes external targets, or invokes an AI API.
The production gateway's current default wildcard bind is preserved; all test clients
and target simulators use loopback and ephemeral ports, with one-time keys. Run on a
trusted test host. Do not treat this as a cross-machine routing test.

## Commands (repository root)

```
dotnet run --project tests/SocketPlatformChecks/SocketPlatformChecks.csproj
dotnet run --project tests/SocketPlatformChecks/SocketPlatformChecks.csproj -- --stress
```

Default suite: nine cases covering 0/1/64 KiB/1 MiB client half-close, 64 KiB target
half-close preserving the reverse channel, silent/partial authentication timeout,
wrong-key refusal, and unapproved target refusal. The 64 KiB response is delayed beyond
the configured idle timeout to exercise heartbeats. Requests use 997-byte writes to
exercise buffering without claiming IP fragmentation coverage.

Stress suite: 25 repeated 1 MiB fragmented-write/half-close exchanges. Any truncated
response, timeout or mismatched bytes fails the process. Do not add retries around a
failed transfer to turn intermittent protocol defects into a passing result.

The workflow uses pinned official checkout/setup-dotnet action commits, read-only
repository permission and standard Windows/macOS runners. No release/tag/deployment,
cache or artifact upload is performed. Source revision and runtime are in job logs.
Public-repository standard-runner compute is free under GitHub's documented policy;
this is not permission to enable larger runners, private paid jobs or other services.

The pull_request trigger runs when these files or application source changes. Manual
workflow_dispatch requires GitHub to recognize the workflow (normally present on the
default branch). Do not merge into main merely to make a manual trigger available;
a draft pull request is sufficient for the PR trigger, subject to repository policy.

## Evidence limits

Passing Windows/macOS jobs verifies core behavior on those operating systems, not
actual Windows-to-macOS LAN communication, hardware SDK behavior, routing/NAT,
Wi-Fi discovery, packet-loss topology, WPF interaction, or a usable macOS GUI.
The Windows job builds WPF but intentionally does not launch it or accept UAC.
