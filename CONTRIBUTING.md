# Contributing

Thanks for considering a contribution to Consul Change Logger.

## Development Prerequisites

- .NET 10 SDK
- Docker Desktop or Docker Engine
- PowerShell, Bash, or another shell that can run the documented commands

## Local Validation

Run these before opening a pull request:

```powershell
dotnet build ConsulChangeLogger.slnx
dotnet run --project tests\ConsulChangeLogger.Tests\ConsulChangeLogger.Tests.csproj
docker build -f src\ConsulChangeLogger.Proxy\Dockerfile -t consul-change-logger:local .
```

The current test project is a lightweight console runner. If you add behavior with meaningful branching, add a focused test there or migrate the relevant area to a standard test framework as part of the same change.

## Pull Request Guidelines

- Keep changes scoped to one concern.
- Document behavior changes in `README.md` or `docs/`.
- Do not commit generated local state such as `outbox/`, `data-protection/`, `bin/`, or `obj/`.
- Do not add secrets or environment-specific values to examples.

## Coding Guidelines

- Keep `Program.cs` as a composition root.
- Put Consul forwarding behavior under `src/ConsulChangeLogger.Proxy/Proxying`.
- Put LDAP and login behavior under `src/ConsulChangeLogger.Proxy/Authentication`.
- Put change record delivery behavior under `src/ConsulChangeLogger.Proxy/ChangeLogging`.
- Keep shared value parsing and event models under `src/ConsulChangeLogger.Core`.
