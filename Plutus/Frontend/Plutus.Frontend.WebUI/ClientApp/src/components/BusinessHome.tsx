import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';
import { useSelector, useDispatch } from 'react-redux';

type Business = {
    name: string,
    nameAbbr: string
};

type AuthState = {
    isAuthenticated: boolean,
    token: string
};


const BusinessHome: React.FC<{ token: string }> = (props) => {
    const [Business, setBusiness] = useState([]);
    const [stores, setStores] = useState([]);
    const token = useSelector((state: AuthState) => state.token);
    console.log("TOKEN INSIDE Business HOME", token);
    const FetchBusiness: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Business/b229d0db-9a4e-456d-a5c0-a28e68324053', {
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
            
            setBusiness(data);
            //setStores(data.stores);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }
    //FetchBusiness(props.token);
    
    useEffect(() : any => {
        FetchBusiness();
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

    console.log("Business!!!", Business);
    console.log("Stores!!!", stores);


    return (
        <>
            <div>
                <p>Business Name : {Business.name}</p>
                <p>Business Abbr : {Business.nameAbbr}</p>
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



export default BusinessHome;