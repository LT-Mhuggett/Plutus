import http from 'k6/http';
import { sleep } from 'k6';


/*const AZURE_TENANT_ID = 'AZURE_APP_DIRECTORY_ID';
const AZURE_CLIENT_ID = '131ab37b-4251-4c37-b0cc-ce3aaf390de2';
const AZURE_CLIENT_SECRET = 'AZURE_APP_CLIENT_SECRET';
const USERNAME = 'USERNAME';
const PASSWORD = 'PASSWORD';
const RESOURCE = 'RESOURCE_ID_URI';
const AZURE_SCOPES =
    'email openid profile https://graph.microsoft.com/User.Read https://graph.microsoft.com/User.ReadBasic.All';*/

/*function setup() {
    // Use either password authentication flow
    let passwordAuthResp = authenticateUsingAzure(AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_SCOPES, {
        username: USERNAME,
        password: PASSWORD,
    });

    return passwordAuthResp;

    // Or client credentials authentication flow
    // let clientAuthResp = authenticateUsingAzure(
    //     AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_SCOPES, RESOURCE
    // );
    // return clientAuthResp;

    // // Example of Okta OAuth password authentication flow
    // let oktaPassAuth = authenticateUsingOkta(OKTA_DOMAIN, 'default', OKTA_CLIENT_ID, OKTA_CLIENT_SECRET, OKTA_SCOPES,
    // {
    //     username: USERNAME,
    //     password: PASSWORD
    // });
    // // This should print the authentication tokens
    // console.log(JSON.stringify(oktaPassAuth));
    // return oktaPassAuth;
}*/
   

    
/**
 * Authenticate using OAuth against Azure Active Directory
 * @function
 * @param  {string} tenantId - Directory ID in Azure
 * @param  {string} clientId - Application ID in Azure
 * @param  {string} clientSecret - Can be obtained from https://docs.microsoft.com/en-us/azure/storage/common/storage-auth-aad-app#create-a-client-secret
 * @param  {string} scope - Space-separated list of scopes (permissions) that are already given consent to by admin
 * @param  {string} resource - Either a resource ID (as string) or an object containing username and password
 *//*
function authenticateUsingAzure(tenantId, clientId, clientSecret, scope, resource) {
    let url;
    const requestBody = {
        client_id: clientId,
        client_secret: clientSecret,
        scope: scope,
    };

    if (typeof resource == 'string') {
        url = `https://login.microsoftonline.com/${tenantId}/oauth2/token`;
        requestBody['grant_type'] = 'client_credentials';
        requestBody['resource'] = resource;
    } else if (
        typeof resource == 'object' &&
        resource.hasOwnProperty('username') &&
        resource.hasOwnProperty('password')
    ) {
        url = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`;
        requestBody['grant_type'] = 'password';
        requestBody['username'] = resource.username;
        requestBody['password'] = resource.password;
    } else {
        throw 'resource should be either a string or an object containing username and password';
    }

    let response = http.post(url, requestBody);

    return response.json();
}*/

export let options = {
    insecureSkipTLSVerify: true,
    noConnectionReuse: false,
    vus: 1,
    duration: '10s'
};

export default () => {

    let params = {
        cookies: { my_cookie: 'value' },
        headers: { 'Authorization': 'Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Im5PbzNaRHJPRFhFSzFqS1doWHNsSFJfS1hFZyJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MjkxMjEzNTcsIm5iZiI6MTYyOTEyMTM1NywiZXhwIjoxNjI5MTI1MjU3LCJhaW8iOiJBWVFBZS84VEFBQUFSRW8xcWxiRGhnOVY3TzJPS2xlK2hnbmpKZWJTQk9abXNKRWJtbERCTWd5dTc3c0NHNlUrM2U3cm01QTkxSXZPRUNKU2thbGQ1aVBMbmpRc05xS2lHdHMxYzNvUWpmbUJ4azhDdHJid09rWXZFMTVQQnVRY3h1ako1R0hyL095dEV6bkIyejB4WTVhRm4yNXhKSWQ3SVdnclJGcDFncmJMVFdLbnUzd1p3ZlE9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6Ikt4Y3RSalY4TjBhOXozZnBiaTB6QmciLCJ2ZXIiOiIyLjAifQ.XQ6gvFARe0IliSMCZwFv22abrPHpPFZEFvFKeHRdu85KA_1UhpU_wYbJDnb5FR0Y3d0baBtfRV3oTRWhx-hXDFSeTI9exhlUlQBGnqgjzVHBjL8PJww-f3HYkCxtlzdDTBOLK3WTJqOPsA3EptU7nQYKnPRlt_88fmWpXCC22aqT-f_7PUWhqGD0j9iaz0wKEksGE8woZsODoNn0t1U8oIfoBYIFN0DoIg7THQ3VAZD3FjohSWTsR_Y7JJKsyR3PbY_cMcjrNXECSCh9fOsjXRVmJc86Wzis9jmrZ8EijNIVY7gE1fDyNhHmDyv11M1-2PfSv1-L9--begM5ATZgDg' },
        redirects: 5,
        tags: { k6test: 'yes' },
    };

    http.get('http://localhost:58559/api/AuthAction/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Bussiness/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Category/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Discount/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Employee/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Item/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Note/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/PaymentMethod/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Refund/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Sale/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/SavedTransaction/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Store/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Stock/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Tax/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Till/Index', params);
    sleep(1);
    http.get('http://localhost:58559/api/Transaction/Index', params);
    sleep(1);
};