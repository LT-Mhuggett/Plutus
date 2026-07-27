import http from 'k6/http';
import { sleep } from 'k6';


const AZURE_TENANT_ID = 'ed398300-920d-4d36-9cde-5d3937f19b7b';
const AZURE_CLIENT_ID = '131ab37b-4251-4c37-b0cc-ce3aaf390de2';
const AZURE_CLIENT_SECRET = 'N9sSbvitt~aJN4XlNNCb5_HX.22_B77uq0';
const USERNAME = 'isid@plutusdevenv.onmicrosoft.com';
const PASSWORD = 'Anshul@123';
const RESOURCE = 'https://plutusdevenv.onmicrosoft.com/131ab37b-4251-4c37-b0cc-ce3aaf390de2';
const AZURE_SCOPES = 'https://plutusdevenv.onmicrosoft.com/131ab37b-4251-4c37-b0cc-ce3aaf390de2/Things.Read https://plutusdevenv.onmicrosoft.com/131ab37b-4251-4c37-b0cc-ce3aaf390de2/OtherThings.Read https://plutusdevenv.onmicrosoft.com/131ab37b-4251-4c37-b0cc-ce3aaf390de2/Permission.Write';

export function setup() {
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
}
   

    
/**
 * Authenticate using OAuth against Azure Active Directory
 * @function
 * @param  {string} tenantId - Directory ID in Azure
 * @param  {string} clientId - Application ID in Azure
 * @param  {string} clientSecret - Can be obtained from https://docs.microsoft.com/en-us/azure/storage/common/storage-auth-aad-app#create-a-client-secret
 * @param  {string} scope - Space-separated list of scopes (permissions) that are already given consent to by admin
 * @param  {string} resource - Either a resource ID (as string) or an object containing username and password
 */
function authenticateUsingAzure(tenantId, clientId, clientSecret, scope, resource) {
    let url;
    const requestBody = {
        client_id: clientId,
        client_secret: clientSecret,
        scope: scope,
        admin_consent: true
    };
    if (typeof resource == 'string') {
        //https://login.microsoftonline.com/ed398300-920d-4d36-9cde-5d3937f19b7b/oauth2/v2.0/authorize
        url = `https://login.microsoftonline.com/${tenantId}/oauth2/v2.0/token`;
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
}

export let options = {
    insecureSkipTLSVerify: true,
    noConnectionReuse: false,
    vus: 1,
    duration: '10s'
};

export default (data) => {

   /* let params = {
        cookies: { my_cookie: 'value' },
        headers: { 'Authorization': 'Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Im5PbzNaRHJPRFhFSzFqS1doWHNsSFJfS1hFZyJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MjkxMjEzNTcsIm5iZiI6MTYyOTEyMTM1NywiZXhwIjoxNjI5MTI1MjU3LCJhaW8iOiJBWVFBZS84VEFBQUFSRW8xcWxiRGhnOVY3TzJPS2xlK2hnbmpKZWJTQk9abXNKRWJtbERCTWd5dTc3c0NHNlUrM2U3cm01QTkxSXZPRUNKU2thbGQ1aVBMbmpRc05xS2lHdHMxYzNvUWpmbUJ4azhDdHJid09rWXZFMTVQQnVRY3h1ako1R0hyL095dEV6bkIyejB4WTVhRm4yNXhKSWQ3SVdnclJGcDFncmJMVFdLbnUzd1p3ZlE9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6Ikt4Y3RSalY4TjBhOXozZnBiaTB6QmciLCJ2ZXIiOiIyLjAifQ.XQ6gvFARe0IliSMCZwFv22abrPHpPFZEFvFKeHRdu85KA_1UhpU_wYbJDnb5FR0Y3d0baBtfRV3oTRWhx-hXDFSeTI9exhlUlQBGnqgjzVHBjL8PJww-f3HYkCxtlzdDTBOLK3WTJqOPsA3EptU7nQYKnPRlt_88fmWpXCC22aqT-f_7PUWhqGD0j9iaz0wKEksGE8woZsODoNn0t1U8oIfoBYIFN0DoIg7THQ3VAZD3FjohSWTsR_Y7JJKsyR3PbY_cMcjrNXECSCh9fOsjXRVmJc86Wzis9jmrZ8EijNIVY7gE1fDyNhHmDyv11M1-2PfSv1-L9--begM5ATZgDg' },
        redirects: 5,
        tags: { k6test: 'yes' },
    };*/
    let params = {
        headers: {
            'Content-Type': 'application/json',
            Authorization: `Bearer ${data.access_token}`, // or `Bearer ${clientAuthResp.access_token}`
        },
    };

    http.get('http://localhost:58559/api/AuthAction/Index', params);
    http.get('http://localhost:58559/api/Bussiness/Index', params);
    http.get('http://localhost:58559/api/Category/Index', params);
    http.get('http://localhost:58559/api/Discount/Index', params);
    http.get('http://localhost:58559/api/Employee/Index', params);
    http.get('http://localhost:58559/api/Item/Index', params);
    http.get('http://localhost:58559/api/Note/Index', params);
    http.get('http://localhost:58559/api/PaymentMethod/Index', params);
    http.get('http://localhost:58559/api/Refund/Index', params);
    http.get('http://localhost:58559/api/Sale/Index', params);
    http.get('http://localhost:58559/api/SavedTransaction/Index', params);
    http.get('http://localhost:58559/api/Store/Index', params);
    http.get('http://localhost:58559/api/Stock/Index', params);
    http.get('http://localhost:58559/api/Tax/Index', params);
    http.get('http://localhost:58559/api/Till/Index', params);
    http.get('http://localhost:58559/api/Transaction/Index', params);
    http.get('http://localhost:58559/api/Bussiness/1', params);
    http.get('http://localhost:58559/api/Category/355', params);
    http.get('http://localhost:58559/api/Discount/133', params);
    http.get('http://localhost:58559/api/Employee/296a9bd3-bf50-43c2-b4f8-65bfbab13cf0', params);
    http.get('http://localhost:58559/api/Note/1001', params);
    http.get('http://localhost:58559/api/PaymentMethod/1', params);
    http.get('http://localhost:58559/api/SavedTransaction/8eb12f3d-1fdb-4c94-8607-9d3f29c963d2', params);
    http.get('http://localhost:58559/api/Store/cdb57cad-9b07-4a74-8983-2a226d220ab7', params);
    http.get('http://localhost:58559/api/Till/228cc3f7-1220-41d4-a21b-c19ed10dca7f', params);
    sleep(1);
};