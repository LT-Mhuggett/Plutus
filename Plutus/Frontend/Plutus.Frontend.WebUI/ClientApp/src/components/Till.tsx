import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';
import { useSelector, useDispatch } from 'react-redux';

type AuthState = {
    isAuthenticated: boolean,
    token: string
};

const Till: React.FC<{ token: string }> = (props) => {
    const token = useSelector((state: AuthState) => state.token);

    const [till, setTill] = useState([]);
    let tillId = props.match.params.tillId;

    const FetchTill: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Till/'.concat(tillId), {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(token)
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });

            if (!response.ok) {
                throw new Error('Something went wrong!');
            }

            const data = await response.json();

            setTill(data);
            //setStores(data.stores);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }
    // FetchBussiness(props.token);

    useEffect((): any => {
        FetchTill();
    }, [])

    //console.log("BUSSINESS!!!", bussiness);
    //console.log("Stores!!!", stores);


    return (
        <>
            <div>
                <h3>Till Details</h3>
                <p>Cash Float : {till.cashFloat}</p>
                <p>Last Online : {till.lastOnline}</p>
            </div>
        </>
    );
}



export default Till;