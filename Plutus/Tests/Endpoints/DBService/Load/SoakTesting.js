import http from 'k6/http';
import { sleep } from 'k6';

function getRequiredEnv(name) {
    const value = __ENV[name];
    if (!value) {
        throw new Error(`Missing required env var: ${name}`);
    }
    return value;
}

const AZURE_TENANT_ID = getRequiredEnv('AZURE_TENANT_ID');
const AZURE_CLIENT_ID = getRequiredEnv('AZURE_CLIENT_ID');
const AZURE_CLIENT_SECRET = getRequiredEnv('AZURE_CLIENT_SECRET');
const USERNAME = getRequiredEnv('AZURE_USERNAME');
const PASSWORD = getRequiredEnv('AZURE_PASSWORD');
const RESOURCE = getRequiredEnv('AZURE_RESOURCE');
const AZURE_SCOPES = getRequiredEnv('AZURE_SCOPES');

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
    console.log(JSON.stringify(response.json()))
    return response.json();
}

export let options = {
  stages: [
    { duration: '2m', target: 400 }, // ramp up to 400 users
    { duration: '3h56m', target: 400 }, // stay at 400 for ~4 hours
    { duration: '2m', target: 0 }, // scale down. (optional)
  ],
};

const BASE_URL = 'http://localhost:58559/api';

export default function (data) {

    let params = {
        'User-Agent': 'k6',
        Authorization: `Bearer ${data.access_token}`, // or `Bearer ${clientAuthResp.access_token}`
    };

    let responses = http.batch([
        [
            'GET',
            `${BASE_URL}/Till/228cc3f7-1220-41d4-a21b-c19ed10dca7f/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Store/cdb57cad-9b07-4a74-8983-2a226d220ab7/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/SavedTransaction/8eb12f3d-1fdb-4c94-8607-9d3f29c963d2/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/PaymentMethod/1/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Note/1001/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Employee/296a9bd3-bf50-43c2-b4f8-65bfbab13cf0/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Discount/133/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Category/355/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Bussiness/1/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/AuthAction/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Bussiness/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Category/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Discount/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Employee/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Item/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Note/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/PaymentMethod/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Refund/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Sale/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/SavedTransaction/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Store/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Stock/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Tax/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Till/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Transaction/Index/`,
            null,
            { headers: params },
        ]
    ]);

    sleep(1);
}
