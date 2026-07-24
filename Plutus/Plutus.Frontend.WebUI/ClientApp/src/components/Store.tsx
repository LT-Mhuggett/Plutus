import React, { useState, useEffect, useCallback, useRef, Component } from 'react';

type Store = {
    contact: string,
    fulladdress: string
};

const Store: React.FC<{ token: string, storeId : string}> = (props) => {
    
    const [store, setStore] = useState([]);
    const [tills, setTills] = useState([]);
    const [employees, setEmployees] = useState([]);

    const FetchStore: any = async (storeId) => {
        try {
            const response = await fetch('http://localhost:58559/api/Store/storeId', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(props.token)
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });

            if (!response.ok) {
                throw new Error('Something went wrong!');
            }

            const data = await response.json();

            setStore(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchStore(props.token);
    }, [])

    const FetchTills: any = async () => {
        try {
            const response = await fetch('http://localhost:58559/api/Till/Index', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(props.token)
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });

            if (!response.ok) {
                throw new Error('Something went wrong!');
            }

            const data = await response.json();

            setTills(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchTills();
    }, [])

    const FetchEmployees: any = async () => {
        try {
            const response = await fetch('http://localhost:58559/api/Employee/Index', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(props.token)
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });

            if (!response.ok) {
                throw new Error('Something went wrong!');
            }

            const data = await response.json();

            setEmployees(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchEmployees();
    }, [])

    return (
        <>
            <h3>Store</h3>
            < table >
                <tr>
                    <th>Contact Number</th>
                    <th>Store Address</th>
                </tr>
                {
                    store.map((store) => (
                        <tr>
                            <td>{store.contactnumber}</td>
                            <td>{store.fulladdress}</td>
                        </tr>))
                }
            </table>

            <h3>Tills</h3>
            < table >
                <tr>
                    <th>LastOnline</th>
                </tr>
                {
                    tills.map((till) => (
                        <tr>
                            <td>{till.lastOnline}</td>
                        </tr>))
                }
            </table>

            <h3>Employees</h3>
            < table >
                <tr>
                    <th>Full Name</th>
                </tr>
                {
                    employees.map((employee) => (
                        <tr>
                            <td>{employee.fname} {employee.lname}</td>
                        </tr>))
                }
            </table>
        </>
    );
}



export default Store;