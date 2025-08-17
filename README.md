# 📂 LAN File Sharer

[![.NET](https://img.shields.io/badge/.NET-9-blueviolet?logo=dotnet)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows-blue?logo=windows)](https://www.microsoft.com/windows)
[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Build](https://img.shields.io/badge/Build-Passing-brightgreen)](#)
[![Contributions](https://img.shields.io/badge/Contributions-Welcome-orange)](#)

**LAN File Sharer** is a simple and efficient Windows utility for transferring files, folders, and text snippets between devices on the same local network.  
Built with **WPF**, it provides an intuitive user interface and a seamless experience for quick and easy content sharing — without relying on cloud services or external drives.

![Screenshot Placeholder](./assets/screenshot.png)  

---

## ✨ Features

This application is packed with features to make local file sharing as easy as possible:

### 🔎 Auto-Discovery
- Automatically discovers other devices on the network running the application.

### 📦 Versatile Transfers
- Send and receive **files**, **entire folders**, and **plain text or URLs**.

### 🖱️ Drag & Drop
- **Drag In:** Drag files and folders directly onto the main window to select them for sending.  
- **Drag Out:** Drag completed files from the transfers list directly to your desktop or a folder.

### 🪟 Windows Shell Integration
- **"Send To" Menu:** Right-click any file or folder in Windows Explorer and send it directly with the application.  
- **Single Instance:** Only one instance runs. Using the "Send To" menu while the app is open will pass the files to the existing window.

### 🎨 Modern UI & Theming
- **Light & Dark Themes** available.  
- **Auto-Sync with Windows:** Matches your system’s light/dark mode automatically.

### ⚡ Quality of Life
- **System Tray Integration:** Minimize to tray to run unobtrusively in the background.  
- **Toast Notifications:** Non-intrusive notifications when transfers are complete.  
- **Exit Confirmation:** Prompts before exiting if transfers are still in progress.  
- **Interactive Transfer List:** Right-click completed transfers to open the file or containing folder.

---

## 🚀 Getting Started

### Installation
The easiest way to get started is by using the provided installer (`.msi` file). The installer will:

- Install the application on your system  
- Create shortcuts on your Desktop and in the Start Menu  
- Integrate the application with the Windows **Send To** context menu  

Simply run the installer and follow the on-screen instructions.

### Usage
1. Launch the **LAN File Sharer** application.  
2. Click the **Refresh** button to discover devices on your network.  
3. Select a device from the **Available Devices** list.  
4. To send content:
   - **Files/Folders:** Click **Browse Files...**, **Browse Folder...**, or drag & drop onto the window.  
   - **Text/URL:** Switch to the **Text / URL** tab and paste your content.  
5. Click **Send**. Incoming transfers will appear automatically in the **Transfers** list.

---

## 🛠️ Building from Source

If you wish to build the application from source, you will need:

- **Visual Studio 2022** (or later) with the **.NET desktop development** workload  
- **.NET 9 SDK** (or the version specified in `LANFileSharing.csproj`)

### Dependencies
These NuGet packages are required and will be restored automatically by Visual Studio:

- [`Hardcodet.NotifyIcon.Wpf`](https://www.nuget.org/packages/Hardcodet.NotifyIcon.Wpf) – for system tray integration  
- [`Ookii.Dialogs.Wpf`](https://www.nuget.org/packages/Ookii.Dialogs.Wpf) – for modern folder browser dialogs  

To build:  
```bash
git clone https://github.com/yourusername/LANFileSharer.git
cd LANFileSharer