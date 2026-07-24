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
    return response.json();
}

export let options = {
    insecureSkipTLSVerify: true,
    noConnectionReuse: false,
    vus: 1,
    duration: '10s'
};

export default (data) => {
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