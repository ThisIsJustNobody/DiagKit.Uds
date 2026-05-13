# Contributing

Thank you for helping improve DiagKit.Uds.

## Development

- Install the .NET SDK version specified by `global.json`.
- Run `dotnet build --no-restore -warnaserror` before submitting changes.
- Run `dotnet test --no-restore` for the full test suite.
- Keep public API changes deliberate and covered by tests.

## Pull Requests

- Describe the behavior change and any compatibility impact.
- Include tests for protocol behavior, failure paths, and regression fixes.
- Keep unrelated formatting or refactoring out of focused fixes.

## Protocol Changes

When changing UDS, DoCAN, or DoIP behavior, cite the relevant ISO behavior in the PR description when possible and add boundary-case tests.
