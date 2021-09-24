import { Route } from 'react-router';
import './custom.css';
import BussinessHome from './components/BussinessHome';
import React, { useState } from "react";
import { PageLayout } from "./components/PageLayout";
import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsal } from "@azure/msal-react";
import { loginRequest } from "./authConfig";
import Button from "react-bootstrap/Button";
import ProfileContent from './components/ProfileContent';

function App() {
    const { instance, accounts, inProgress } = useMsal();
    const [accessToken, setAccessToken] = useState("");

    const name = accounts[0] && accounts[0].name;

    function RequestAccessToken() {
        const request = {
            ...loginRequest,
            account: accounts[0]
        };

        // Silently acquires an access token which is then attached to a request for Microsoft Graph data
        instance.acquireTokenSilent(request).then((response) => {
            setAccessToken(response.accessToken);
        }).catch((e) => {
            instance.acquireTokenPopup(request).then((response) => {
                setAccessToken(response.accessToken);
            });
        });
    }

    return (
        <PageLayout>
            <AuthenticatedTemplate>
                {/*<ProfileContent />*/}

                <section>
                    <BussinessHome token={accessToken} />
                </section>
                <p>You are signed in!</p>
            </AuthenticatedTemplate>
            <UnauthenticatedTemplate>
                <p>You are not signed in! Please sign in.</p>
            </UnauthenticatedTemplate>
        </PageLayout>
    );
}

export default App;
