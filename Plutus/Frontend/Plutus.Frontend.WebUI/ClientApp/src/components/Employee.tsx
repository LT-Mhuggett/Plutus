import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';
import { useSelector, useDispatch } from 'react-redux';

type Employee = {
    fName: string,
};

type AuthState = {
    isAuthenticated: boolean,
    token: string
};


const Employee: React.FC<{ token: string }> = (props) => {
    const [employee, setEmployee] = useState([]);
    let employeeId = props.match.params.employeeId;
    const token = useSelector((state: AuthState) => state.token);

    const FetchEmployee: any = async () => {
        try {
            const response = await fetch('https://localhost:44313/api/Employee/'.concat(employeeId), {
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

            setEmployee(data);
            //setStores(data.stores);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }
    // FetchBussiness(props.token);

    useEffect((): any => {
        FetchEmployee();
    }, [])

    //console.log("BUSSINESS!!!", bussiness);
    //console.log("Stores!!!", stores);


    return (
        <>
            <div>
                <h3>Employee Details</h3>
                <p>First Name : {employee.fName}</p>
                <p>Last Name : {employee.lName}</p>
                <p>Email : {employee.email}</p>
                <p>Contact : {employee.mobile}</p>
            </div>
        </>
    );
}



export default Employee;