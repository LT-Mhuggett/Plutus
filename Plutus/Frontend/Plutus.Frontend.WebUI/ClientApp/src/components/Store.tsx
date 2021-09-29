import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';

type Store = {
    name: string,
};

const Store: React.FC<{ token: string }> = (props, match) => {
    const [store, setStore] = useState([]);
    const [employees, setEmployees] = useState([]);
    const [tills, setTills] = useState([]);


    let storeId = props.match.params.storeId;
    let tempToken = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzI5MjMzNTksIm5iZiI6MTYzMjkyMzM1OSwiZXhwIjoxNjMyOTI3MjU5LCJhaW8iOiJBWVFBZS84VEFBQUFYRUdiRE91MGZoVEh0WWwvcDZkWkN1UzhwSnBkWFJTa2ZQeDNkTWJoRVExTWV1SEdQTEwzc0gxc1hWOVdZb2VXRmw5QVpPN2N2TmdudmFlRUR0UGRua0NKZFZRUEFWR3BVNXpMY2NJZGlOMFkwWGR4bDQvenRMUGZBZXBKRkZDdklJMnI1ZVo5WEpsVWxNci9DQ3AvNCtuaEJORjYwQS9MQXRKQTJNMUxYT2M9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6ImdmTVpJMXNfaVVhdmFFY1lqX3ZBQVEiLCJ2ZXIiOiIyLjAifQ.HarzFEpxB-CgPZf8750X2nG_YzQZr7N6gt_VWavOs2J3T7_17t5kxf6xMvVerksz21PJY_1fgrn4xySdSIszHEeDLZpSPtNtjUuui8NW-Y6NpadCmZfPzAunun0x4nIZrJKWeSkzSsroUZwbRi7FMFlErSb5_6RBqZph71FcPl0C2pQyuc1wVAJl4DeefJ5H0XmB251od1NOB1zaZ1KLIixZMmzIVirEeBGigZlIPcQu0SDI8MVbfnVTO9mGQImYcqN-qQdYyw6UAvbFIA1GHH9znce_okzcxiXfV8zxX1hVq4KqImbJxvVChZLc1e1akIgRAYiHGd0bq2JP072g4g";
    const FetchStore: any = async () => {
        try {
            const response = await fetch('http://localhost:58559/api/Store/'.concat(storeId), {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(tempToken)
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
            const response = await fetch('http://localhost:58559/api/Employee/Index', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(tempToken)
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
            const response = await fetch('http://localhost:58559/api/Till/Index', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat(tempToken)
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