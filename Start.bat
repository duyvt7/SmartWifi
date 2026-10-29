@echo off
chcp 65001 >nul
title Crane Wi-Fi Controller - Khởi Động
setlocal enabledelayedexpansion

:: 1. Check Administrator Privileges
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [THÔNG BÁO] Đang yêu cầu quyền Administrator để tối ưu card mạng cẩu...
    powershell -Command "Start-Process cmd.exe -ArgumentList '/c \"\"%~f0\"\"' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

:: 2. Check if pre-compiled single file exists
if exist "%~dp0App_SingleFile\CraneWifiController.exe" (
    echo [OK] Tìm thấy file thực thi CraneWifiController.exe (827 KB).
    echo Đang khởi chạy Crane Wi-Fi Controller...
    start "" "%~dp0App_SingleFile\CraneWifiController.exe"
    exit /b
)

:: 3. If not found, try building and running from source using dotnet
echo [KIỂM TRA] Đang kiểm tra môi trường .NET trên máy...

where dotnet >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] Đang biên dịch và chạy từ mã nguồn...
    dotnet run --project "%~dp0src\Presentation\CraneWifi.WidgetApp\CraneWifi.WidgetApp.csproj" -c Release
    exit /b
)

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    echo [OK] Đang biên dịch và chạy bằng .NET SDK người dùng...
    "%USERPROFILE%\.dotnet\dotnet.exe" run --project "%~dp0src\Presentation\CraneWifi.WidgetApp\CraneWifi.WidgetApp.csproj" -c Release
    exit /b
)

:: 4. If neither exists, show instructions
echo.
echo =========================================================================
echo [LỖI] Không thể khởi động Crane Wi-Fi Controller!
echo.
echo Nguyên nhân: Máy tính của bạn chưa có sẵn .NET 8.0 Desktop Runtime.
echo.
echo Hướng dẫn khắc phục:
echo   1. Tải và cài đặt ".NET Desktop Runtime 8.0 (x64)" từ Microsoft:
echo      https://dotnet.microsoft.com/download/dotnet/8.0/runtime
echo   2. Sau đó mở lại file Start.bat này.
echo =========================================================================
echo.
pause
exit /b
