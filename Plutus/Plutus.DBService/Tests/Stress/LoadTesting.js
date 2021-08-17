import http from 'k6/http';
import { check, group, sleep } from 'k6';

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
    stages: [
        { duration: '5m', target: 100 }, // simulate ramp-up of traffic from 1 to 100 users over 5 minutes.
        { duration: '10m', target: 100 }, // stay at 100 users for 10 minutes
        { duration: '5m', target: 0 }, // ramp-down to 0 users
    ],
    thresholds: {
        http_req_duration: ['p(99)<1500'], // 99% of requests must complete below 1.5s
        //'logged in successfully': ['p(99)<1500'], // 99% of requests must complete below 1.5s
    },
};

const BASE_URL = 'http://localhost:58559/api';

export default (data) => {
    let params = {
        cookies: { my_cookie: 'value' },
        headers: {
            Authorization: `Bearer ${data.access_token}`, // or `Bearer ${clientAuthResp.access_token}`
        },
        redirects: 5,
        tags: { k6test: 'yes' },
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
    sleep(1);
};
