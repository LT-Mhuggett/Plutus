export const msalConfig = {
    auth: {
        clientId: "131ab37b-4251-4c37-b0cc-ce3aaf390de2",
        authority: "https://login.microsoftonline.com/ed398300-920d-4d36-9cde-5d3937f19b7b",
        redirectUri: "https://localhost:44307/",
    },
    cache: {
        cacheLocation: "sessionStorage", // This configures where your cache will be stored
        storeAuthStateInCookie: false, // Set this to "true" if you are having issues on IE11 or Edge
    }
};

// Add scopes here for ID token to be used at Microsoft identity platform endpoints.
export const loginRequest = {
    //scopes: ["Things.Read", "OtherThings.Read", "WritePermission.Write"]
    scopes: ["User.Read"]
};

// Add the endpoints here for Microsoft Graph API services you'd like to use.
export const graphConfig = {
    graphMeEndpoint: "Enter_the_Graph_Endpoint_Here/v1.0/me"
};