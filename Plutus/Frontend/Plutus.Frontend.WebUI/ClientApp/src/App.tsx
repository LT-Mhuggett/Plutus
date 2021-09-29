import './custom.css';
import BussinessHome from './components/BussinessHome';
import Store from './components/Store';
import Employee from './components/Employee';
import Till from './components/Till';
import React, { useState } from "react";
import { PageLayout } from "./components/PageLayout";
import { AuthenticatedTemplate, UnauthenticatedTemplate, useMsal } from "@azure/msal-react";
import { loginRequest } from "./authConfig";
import Button from "react-bootstrap/Button";
import ProfileContent from './components/ProfileContent';
import { BrowserRouter as Router, Route, Link } from 'react-router-dom';
import routes from './config/routes';

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
                {/*<BrowserRouter>
                    <Switch>
                        {routes.map((route, index) => {
                            return (
                                <Route
                                    key={index},
                                    path = { route.path },
                                    exact = { route.exact },
                                    render = {(props: RouteComponentProps<any>)=>(
                                        <route.component
                                            name={ route.name}
                                            {...props}
                                            {route.props}
                                            token={accessToken}
                                        />
                                    )}
                                />
                                );
                        })}
                    </Switch>
                </BrowserRouter>*/}
                {/*<ProfileContent />*/}

                  
                   {/* <Link to="/">BussinessHome</Link>
                    <Link to="/store">Store</Link>*/}
                
                    <Route path="/" exact component={BussinessHome} />
                <Route path="/store/:storeId" exact component={Store} />
                <Route path="/employee/:employeeId" exact component={Employee} />
                <Route path="/till/:tillId" exact component={Till} />

                <p>You are signed in!</p>
            </AuthenticatedTemplate>
            <UnauthenticatedTemplate>
                <p>You are not signed in! Please sign in.</p>
            </UnauthenticatedTemplate>
        </PageLayout>
    );
}

export default App;
