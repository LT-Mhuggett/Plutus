import React, { useState, useEffect, useCallback, useRef, Component } from 'react';

type Bussiness = {
    name: string,
    nameAbbr: string
};

const BussinessHome: React.FC<{ token: string }> = (props) => {
    const [bussiness, setBussiness] = useState([]);

    const FetchBussiness: any = async (token: string) => {
        try {
            const response = await fetch('http://localhost:58559/api/Bussiness/Index', {
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
            
            setBussiness(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }
    // FetchBussiness(props.token);
    
    useEffect(() : any => {
        FetchBussiness(props.token);
    }, [])
    
    
    console.log("DATA", bussiness);
    return (
        <>
            
            { bussiness.map((b) =>
                (<div>
                    <p>Bussiness Name : {b.name}</p>
                    <p>{props.token}</p>
                <p>Bussiness Abbr : TEST NAME</p>
                <p onClick={ b.id}>Click here to go to Stores</p>
                </div>)
             )} 
           
        </>
    );
}



export default BussinessHome;