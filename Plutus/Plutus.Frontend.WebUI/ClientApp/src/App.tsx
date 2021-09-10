import * as React from 'react';
import { Route } from 'react-router';
import { PageLayout } from "./components/PageLayout";
import './custom.css'
import { AuthenticatedTemplate, UnauthenticatedTemplate } from "@azure/msal-react";

function App() {
    return (
        <PageLayout>
            <AuthenticatedTemplate>
                <p>You are signed in!</p>
            </AuthenticatedTemplate>
            <UnauthenticatedTemplate>
                <p>You are not signed in! Please sign in.</p>
            </UnauthenticatedTemplate>
        </PageLayout>
    );
}

export default App;
