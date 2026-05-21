# Changelog

All notable changes to this project are documented in this file.

## 3.0.0 - Unreleased

- Added typed flashing helpers for RequestDownload, TransferData, and RequestTransferExit.
- Added OEM-friendly SecurityAccess overloads for requestSeed payloads and custom sendKey payload builders.
- Added RoutineControl.StartAndExpectCompletedAsync for predicate-based routine completion polling.
- Added the optional DiagKit.Uds.CanHub bridge package, currently pinned to CanHub.Abstractions 1.0.0-preview.4.
- Added fail-fast handling for UDS server and ECU simulator handler failures.
- Added observable keep-alive and session worker completion tasks.
- Added a configurable DoCAN segmented payload limit.
- Tightened suppressed-response matching and negative-response SID validation.
- Improved DoIP stream disposal ordering during active operations.
- Added standard open-source project governance files.
