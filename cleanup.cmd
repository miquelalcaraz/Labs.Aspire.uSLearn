@echo off
rem Removes build output (bin/obj) under src: every git-ignored file except per-user *.user settings.
rem Usage: cleanup.cmd       remove
rem        cleanup.cmd -n    preview only
set "MODE=-f"
if /i "%~1"=="-n" set "MODE=-n"
git -C "%~dp0." clean -Xd %MODE% -e "!*.user" -- src
