@echo off
chcp 65001 >nul
title Crane Wi-Fi Controller - Khởi Động
setlocal enabledelayedexpansion

:: 1. Kiểm tra quyền Administrator
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [THÔNG BÁO] Đang yêu cầu quyền Administrator để quản lý card mạng cẩu...
    powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

cd /d "%~dp0"

:: 2. Khởi chạy file thực thi siêu nhẹ đóng gói sẵn nếu có
if exist "%~dp0App_SingleFile\CraneWifiController.exe" (
    echo [OK] Tìm thấy file thực thi CraneWifiController.exe (827 KB).
    echo Đang khởi chạy Crane Wi-Fi Controller...
    start "" "%~dp0App_SingleFile\CraneWifiController.exe"
    exit /b
)

:: 3. Nếu chưa có, tự động build và chạy từ mã nguồn bằng .NET SDK
echo [KIỂM TRA] Đang kiểm tra môi trường .NET trên máy tính...

where dotnet >nul 2>&1
if %errorlevel% equ 0 (
    echo [OK] Đang biên dịch và chạy từ mã nguồn qua dotnet SDK...
    dotnet run --project "%~dp0src\Presentation\CraneWifi.WidgetApp\CraneWifi.WidgetApp.csproj" -c Release
    exit /b
)

if exist "%USERPROFILE%\.dotnet\dotnet.exe" (
    echo [OK] Đang biên dịch và chạy bằng .NET SDK người dùng...
    "%USERPROFILE%\.dotnet\dotnet.exe" run --project "%~dp0src\Presentation\CraneWifi.WidgetApp\CraneWifi.WidgetApp.csproj" -c Release
    exit /b
)

:: 4. Nếu máy chưa có .NET Desktop Runtime, kiểm tra xem có file cài đặt sẵn không
if exist "%~dp0windowsdesktop-runtime-8.0-win-x64.exe" (
    echo.
    echo =========================================================================
    echo [THÔNG BÁO] Máy tính chưa có .NET 8.0 Desktop Runtime.
    echo [OK] Tìm thấy file cài đặt có sẵn: windowsdesktop-runtime-8.0-win-x64.exe
    echo.
    echo Đang khởi chạy trình cài đặt .NET 8 Desktop Runtime của Microsoft...
    echo Vui lòng bấm "Install" trong cửa sổ cài đặt vừa xuất hiện.
    echo =========================================================================
    echo.
    "%~dp0windowsdesktop-runtime-8.0-win-x64.exe"
    echo.
    echo Sau khi cài đặt .NET 8 xong, vui lòng nhấp đúp lại vào Start.bat!
    pause
    exit /b
)

echo.
echo =========================================================================
echo [THÔNG BÁO] Không thể khởi động Crane Wi-Fi Controller!
echo.
echo Nguyên nhân: Máy tính của bạn chưa có sẵn .NET 8.0 Desktop Runtime.
echo.
echo Hướng dẫn khắc phục rất đơn giản:
echo   1. Tải và cài đặt ".NET Desktop Runtime 8.0 (x64)" từ Microsoft:
echo      https://dotnet.microsoft.com/download/dotnet/8.0/runtime
echo   2. Sau khi cài xong, nhấp đúp lại vào file Start.bat này.
echo =========================================================================
echo.
pause
exit /b
