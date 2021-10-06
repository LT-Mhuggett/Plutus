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
                    'Authorization': 'Bearer '.concat("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzIzMjE5MDEsIm5iZiI6MTYzMjMyMTkwMSwiZXhwIjoxNjMyMzI1ODAxLCJhaW8iOiJBWVFBZS84VEFBQUFxTUpwTkdQMzNId3hIRFk0QSt5M0tMck9zN0xVU0VSY3ZET1dMdnFleCtsMS9aLyt3SlY1U0ErUXY4VmJkT282NHJZYVNHNmlaR1pSME1KYThxM29TNytpVDVMZDl1a2x4VjFUY0lQT0gyS2lhZjd3SHlyYnJlbmdHNHNoM3diZGVHQmVPUEtRUDdvaHMxQWdYcmhjZi95QlphNHlUeVdtZ0NZaGxrTUpMNk09IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6ImczT25zNGZZdkVXNHoxQk9NSjVxQUEiLCJ2ZXIiOiIyLjAifQ.ceMsr0YP26SzGCTG0dnqjPfO5PhkJWtUJPlBkuevlHeGwfXgYHwCJ_xyvaJpspOO3R3WFRc4G0CHCWd5aQN7DSYs1QyKj6myEesBgn2rEL38kX0kp2h_mrfSdaB9cX554tT3JxkAJliqyGKP2MQ0WjmfVZ3iAcUpsNAjC5UdQCb1M9OkEZ3-V2TuVqhLJoSEMLy1v9fIOMEsXjXvL_YjrP_EH2cR6RZ5Cl6wMULg_9tkrY-K7bIPMMpNP-OegVgK8M7ASpG5v5ZLDzhW6vUo8LNMpxa0z85poZTJjx2YIoT0smdMnkm9HGXM0OcdGSI_l1hP5nuURwwGm7A5YIehAw")
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
                    'Authorization': 'Bearer '.concat("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzIzMjE5MDEsIm5iZiI6MTYzMjMyMTkwMSwiZXhwIjoxNjMyMzI1ODAxLCJhaW8iOiJBWVFBZS84VEFBQUFxTUpwTkdQMzNId3hIRFk0QSt5M0tMck9zN0xVU0VSY3ZET1dMdnFleCtsMS9aLyt3SlY1U0ErUXY4VmJkT282NHJZYVNHNmlaR1pSME1KYThxM29TNytpVDVMZDl1a2x4VjFUY0lQT0gyS2lhZjd3SHlyYnJlbmdHNHNoM3diZGVHQmVPUEtRUDdvaHMxQWdYcmhjZi95QlphNHlUeVdtZ0NZaGxrTUpMNk09IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6ImczT25zNGZZdkVXNHoxQk9NSjVxQUEiLCJ2ZXIiOiIyLjAifQ.ceMsr0YP26SzGCTG0dnqjPfO5PhkJWtUJPlBkuevlHeGwfXgYHwCJ_xyvaJpspOO3R3WFRc4G0CHCWd5aQN7DSYs1QyKj6myEesBgn2rEL38kX0kp2h_mrfSdaB9cX554tT3JxkAJliqyGKP2MQ0WjmfVZ3iAcUpsNAjC5UdQCb1M9OkEZ3-V2TuVqhLJoSEMLy1v9fIOMEsXjXvL_YjrP_EH2cR6RZ5Cl6wMULg_9tkrY-K7bIPMMpNP-OegVgK8M7ASpG5v5ZLDzhW6vUo8LNMpxa0z85poZTJjx2YIoT0smdMnkm9HGXM0OcdGSI_l1hP5nuURwwGm7A5YIehAw")
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
                    'Authorization': 'Bearer '.concat("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzIzMjE5MDEsIm5iZiI6MTYzMjMyMTkwMSwiZXhwIjoxNjMyMzI1ODAxLCJhaW8iOiJBWVFBZS84VEFBQUFxTUpwTkdQMzNId3hIRFk0QSt5M0tMck9zN0xVU0VSY3ZET1dMdnFleCtsMS9aLyt3SlY1U0ErUXY4VmJkT282NHJZYVNHNmlaR1pSME1KYThxM29TNytpVDVMZDl1a2x4VjFUY0lQT0gyS2lhZjd3SHlyYnJlbmdHNHNoM3diZGVHQmVPUEtRUDdvaHMxQWdYcmhjZi95QlphNHlUeVdtZ0NZaGxrTUpMNk09IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6ImczT25zNGZZdkVXNHoxQk9NSjVxQUEiLCJ2ZXIiOiIyLjAifQ.ceMsr0YP26SzGCTG0dnqjPfO5PhkJWtUJPlBkuevlHeGwfXgYHwCJ_xyvaJpspOO3R3WFRc4G0CHCWd5aQN7DSYs1QyKj6myEesBgn2rEL38kX0kp2h_mrfSdaB9cX554tT3JxkAJliqyGKP2MQ0WjmfVZ3iAcUpsNAjC5UdQCb1M9OkEZ3-V2TuVqhLJoSEMLy1v9fIOMEsXjXvL_YjrP_EH2cR6RZ5Cl6wMULg_9tkrY-K7bIPMMpNP-OegVgK8M7ASpG5v5ZLDzhW6vUo8LNMpxa0z85poZTJjx2YIoT0smdMnkm9HGXM0OcdGSI_l1hP5nuURwwGm7A5YIehAw")
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