@echo off
REM يتطلب .NET 8 SDK:  https://dotnet.microsoft.com/download/dotnet/8.0
REM الناتج: dist\YTFolderIcon\YTFolderIcon.exe  (ملف واحد، يحتاج .NET 8 Desktop Runtime على الجهاز)
dotnet publish YTFolderIcon.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -o dist\YTFolderIcon
if errorlevel 1 (echo Build failed & pause & exit /b 1)
echo.
echo Done: dist\YTFolderIcon\YTFolderIcon.exe
pause
