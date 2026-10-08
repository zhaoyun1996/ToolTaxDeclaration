# Project: MISA.ASP.ClientApp

## Overview
MISA.ASP.ClientApp is a C# Windows Forms application for synchronizing tax declarations with Vietnamese tax authority systems. The application connects to two main systems:

1. **thuedientu.gdt.gov.vn** (Electronic Tax System) - For tax declaration registration and submission
2. **dichvucong.gdt.gov.vn** (Public Service Portal) - For tax payment receipt inquiries

## Core Functionality
- Synchronize tax declarations already registered (`SyncTaxDecRegistered`)
- Synchronize tax declarations already submitted (`SyncTaxDecSubmitted`)
- Query tax payment receipts (`SyncPaymentRequest`)
- Check electronic tax account validity (`CheckETaxAccount`)

## Architecture
- **UI Layer**: Windows Forms forms for user interaction
- **BL (Business Logic) Layer**: ETaxCrawler and PublicServiceCrawler for web scraping/automation
- **Models Layer**: Data entities, enums, input/output DTOs
- **Utils Layer**: Clients, converters, design patterns, file handling, logging, URI handling

## Key Features
- Local mode and remote mode support (configurable via `IsLocalMode` app setting)
- Captcha solving via external API or Python CLI
- Progress tracking with progress bar
- File management: upload/download tax declaration files, notifications, documents
- Retry pattern for transient failures
- NLog-based error logging
- Single-instance application via IPC channel

## Project Structure
```
MISA.ASP.ClientApp/
├── UI/                   Windows Forms UI forms
├── BL/                   Business Logic layer (crawlers)
├── Models/               Data models, enums, DTOs
├── Utils/                Utilities (clients, converters, patterns, logging)
├── Program.cs            Entry point with URI handler
├── App.config            Application configuration
└── NLog.config           Logging configuration
```