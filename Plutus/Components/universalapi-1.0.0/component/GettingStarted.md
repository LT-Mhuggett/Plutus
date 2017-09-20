# Getting Started with Univeral API

If you’re new to the CloudRail SDK, you’ll probably want to look first at [CloudRail SDK for Xamarin.Android](https://cloudrail.com/integrations/interfaces/CloudStorage;platformId=XamarinAndroid/) or [CloudRail SDK for Xamarin.iOS](https://cloudrail.com/integrations/interfaces/CloudStorage;platformId=XamarinIOS/) which explains how to set up the SDK and how to get started using CloudRail for Xamarin.Android or Xamarin.iOS.

After you’ve set up the SDK, you can start working with the mobile application for services like Dropbox, Google Drive, One Drive, Box, One Drive Business and Egnyte. Don't forget to register at [CloudRail](https://cloudrail.com/signup) to get your licence key.



### Android Sample

```csharp
// Initialize the SDK first
CloudRail.AppKey = "[YourLicenseKey]";

// Inizialize the service, in this case we are using Dropbox.
Dropbox dropbox = new Dropbox(this,"[Dropbox Client Identifier]","[Dropbox Client Secret]");

// Now we use the method GetChildren with the path Root "/" to get a list of files and folder.
// Note, you have to run this on the background thread.
IList<CloudMetaData> result = dropbox.GetChildren("/");

```

### iOS Sample

```csharp
// Initialize the SDK first
CRCloudRail.AppKey = "[YourLicenseKey]";

// Inizialize the service, in this case we are using Dropbox.
CRDropbox dropbox = new CRDropbox("[Dropbox Client Identifier]","[Dropbox Client Secret]");

// Now we use the method ChildrenOfFolderWithPath with the path Root "/" to get a list of files and folder.
// Note, you have to run this on the background thread.
CRCloudMetaData[] result = NSArray.FromArray<CRCloudMetaData>(dropbox.ChildrenOfFolderWithPath("/"));

```


## Additional Resources

- [**Android Code Sample**](https://github.com/CloudRail/cloudrail-si-xamarin-android-sdk/tree/master/Examples/UnifiedCloudStorage) - Repository of example projects using the SDK.
- [**iOS Code Sample**](https://github.com/CloudRail/cloudrail-si-xamarin-ios-sdk/tree/master/Examples/UnifiedCloudStorage) - Repository of example projects using the SDK.
- [**CloudRail Forum**](https://forum.cloudrail.com/) – Ask questions, get help, and give feedback
- [**CloudRail Tutorials**](https://cloudrail.com/tutorials) – Watch tutorial videos or read blogs 
- [**CloudRail Blog**](https://blog.cloudrail.com/) – Keep up to date with the latest news

