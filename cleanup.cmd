@echo off
rem Removes bin/obj folders and empty directories under src. Works from any working directory.
call "%~dp0sln-tools\cleanup.cmd" %*
