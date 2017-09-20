Univeral API (CloudRail SDK), provides developers a unified solution for cloud storage sevices such as Dropbox, Google Drive, One Drive, Box, One Drive Business and Egnyte.

## Univeral API (CloudRail SDK) for Xamarin

The Univeral API (CloudRail SDK) for Xamarin provides a set of .NET libraries, code samples, and documentation to help developers build mobile applications for iOS and Android. Mobile apps written using Xamarin call native platform APIs so they have the look and feel of native applications. The .NET libraries in the SDK provide C# wrappers around the CloudRail SDK.

## Getting Started

Univeral API (CloudRail SDK) is very easy to use. You only need to use the Unified Interface to work with all the services.

### Android Sample

```csharp
// Initialize the SDK first
CloudRail.AppKey = "[YourLicenseKey]";

ICloudStorage service;

Box box = new Box(this, "[Box Client Identifier]", "[Box Client Secret]");

OneDrive onedrive = new OneDrive(this, "[OneDrive Client Identifier]", "[OneDrive Client Secret]");

// 'selection' is a String representing e.g. a user's service choice
switch(selection) {
    case "box": service = box; break;
    case "onedrive": service = onedrive; break;
}

Stream result = service.Download("/myFolder/myFile.png");

```

### iOS Sample

```csharp
// Initialize the SDK first
CloudRail.AppKey = "[YourLicenseKey]";

ICRCloudStorage service;

CRBox box = new CRBox("[Box Client Identifier]", "[Box Client Secret]");

CROneDrive onedrive = new CROneDrive("[OneDrive Client Identifier]", "[OneDrive Client Secret]");

// 'selection' is a String representing e.g. a user's service choice
switch(selection) {
    case "box": service = box; break;
    case "onedrive": service = onedrive; break;
}

NSInputStream result = service.DownloadFileWithPath("/myFolder/myFile.png");

```
