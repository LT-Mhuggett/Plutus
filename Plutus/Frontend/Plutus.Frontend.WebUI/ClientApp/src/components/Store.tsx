import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';
import { useSelector, useDispatch } from 'react-redux';

type Store = {
    name: string,
};

type AuthState = {
    isAuthenticated: boolean,
    token: string
};

const Store: React.FC<{ token: string }> = (props, match) => {
    const token = useSelector((state: AuthState) => state.token);
    const [store, setStore] = useState([]);
    const [employees, setEmployees] = useState([]);
    const [tills, setTills] = useState([]);


    let storeId = props.match.params.storeId;

    const FetchStore: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Store/'.concat(storeId), {
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

            setStore(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchStore();
    }, [])

    const FetchEmployees: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Employee/Index', {
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

            setEmployees(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchEmployees();
    }, [])

    const FetchTills: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Till/Index', {
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

            setTills(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchTills();
    }, [])

    //console.log("BUSSINESS!!!", bussiness);
    console.log("Store!!!", store);


    return (
        <>
            <div>
                <p>Store City : {store.city}</p>
                <p>Store Address : {store.readableAddress}</p>
            </div>

            <h3>Employees</h3>
            < table >
                <thead>
                    <tr>
                        <th>First Name</th>
                        <th>Last Name</th>
                        <th>Email</th>
                    </tr>
                </thead>
                <tbody>
                    {
                        employees.map((employee) => (
                            <tr>
                                <Link to={`/employee/` + employee.id}>View Employee</Link>
                                <td>{employee.fName}</td>
                                <td>{employee.lName}</td>
                                <td>{employee.email}</td>
                            </tr>)
                        )
                    }
                </tbody>
            </table>

            <h3>Tills</h3>
            < table >
                <thead>
                    <tr>
                        <th>Last Online</th>
                        <th>Cash Float</th>
                    </tr>
                </thead>
                <tbody>
                    {
                        tills.map((till) => (
                            <tr>
                                <Link to={`/till/` + till.id}>View Till</Link>
                                <td>{till.lastOnline}</td>
                                <td>{till.cashFloat}</td>
                            </tr>)
                        )
                    }
                </tbody>
            </table>

        </>
    );
}



export default Store;