import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';
import { useSelector, useDispatch } from 'react-redux';

type Bussiness = {
    name: string,
    nameAbbr: string
};

type AuthState = {
    isAuthenticated: boolean,
    token: string
};


const BussinessHome: React.FC<{ token: string }> = (props) => {
    const [bussiness, setBussiness] = useState([]);
    const [stores, setStores] = useState([]);
    const token = useSelector((state: AuthState) => state.token);
    console.log("TOKEN INSIDE BUSSINESS HOME", token);
    const FetchBussiness: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Bussiness/b229d0db-9a4e-456d-a5c0-a28e68324053', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(token)
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });
            
            if (!response.ok) {
                console.log(response)
                throw new Error('Something went wrong!');
            }

            const data = await response.json();
            
            setBussiness(data);
            //setStores(data.stores);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }
    //FetchBussiness(props.token);
    
    useEffect(() : any => {
        FetchBussiness();
    }, [])
    

    const FetchStores: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Store/Index', {
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
            console.log("DATA", data);
            setStores(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchStores();
    }, [])

    console.log("BUSSINESS!!!", bussiness);
    console.log("Stores!!!", stores);


    return (
        <>
            <div>
                <p>Bussiness Name : {bussiness.name}</p>
                <p>Bussiness Abbr : {bussiness.nameAbbr}</p>
            </div>

            <h3>Stores</h3>
            < table >
                <thead>
                    <tr>
                        <th>Contact Number</th>
                        <th>Store Address</th>
                    </tr>
                </thead>
                <tbody>
                {
                    stores.map((store) => (
                        <tr>
                            <Link to={`/store/` + store.id}>Go to Store</Link>
                            <td>{store.contactNumber}</td>
                            <td>{store.readableAddress}</td>
                        </tr>)
                    )
                }
                </tbody>
            </table>
              
        </>
    );
}



export default BussinessHome;