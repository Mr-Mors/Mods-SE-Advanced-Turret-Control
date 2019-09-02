for %%I in (.) do powershell -ExecutionPolicy ByPass -file D:\Modding\SpaceEngineers\MrMors\Assets\Generate-icons.ps1 "%%~nxI"
for %%I in (.) do powershell -ExecutionPolicy ByPass -file D:\Modding\SpaceEngineers\MrMors\Assets\Deploy.ps1 "%%~nxI"
pause