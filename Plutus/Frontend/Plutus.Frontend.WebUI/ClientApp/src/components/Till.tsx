import React, { useState, useEffect, useCallback, useRef, Component } from 'react';
import { Link } from 'react-router-dom';

const Till: React.FC<{ token: string }> = (props) => {
    const [till, setTill] = useState([]);
    let tillId = props.match.params.tillId;

    let tempToken = "eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzI5MjMzNTksIm5iZiI6MTYzMjkyMzM1OSwiZXhwIjoxNjMyOTI3MjU5LCJhaW8iOiJBWVFBZS84VEFBQUFYRUdiRE91MGZoVEh0WWwvcDZkWkN1UzhwSnBkWFJTa2ZQeDNkTWJoRVExTWV1SEdQTEwzc0gxc1hWOVdZb2VXRmw5QVpPN2N2TmdudmFlRUR0UGRua0NKZFZRUEFWR3BVNXpMY2NJZGlOMFkwWGR4bDQvenRMUGZBZXBKRkZDdklJMnI1ZVo5WEpsVWxNci9DQ3AvNCtuaEJORjYwQS9MQXRKQTJNMUxYT2M9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6ImdmTVpJMXNfaVVhdmFFY1lqX3ZBQVEiLCJ2ZXIiOiIyLjAifQ.HarzFEpxB-CgPZf8750X2nG_YzQZr7N6gt_VWavOs2J3T7_17t5kxf6xMvVerksz21PJY_1fgrn4xySdSIszHEeDLZpSPtNtjUuui8NW-Y6NpadCmZfPzAunun0x4nIZrJKWeSkzSsroUZwbRi7FMFlErSb5_6RBqZph71FcPl0C2pQyuc1wVAJl4DeefJ5H0XmB251od1NOB1zaZ1KLIixZMmzIVirEeBGigZlIPcQu0SDI8MVbfnVTO9mGQImYcqN-qQdYyw6UAvbFIA1GHH9znce_okzcxiXfV8zxX1hVq4KqImbJxvVChZLc1e1akIgRAYiHGd0bq2JP072g4g";

    const FetchTill: any = async (token: string) => {
        try {
            const response = await fetch('http://localhost:58559/api/Till/'.concat(tillId), {
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