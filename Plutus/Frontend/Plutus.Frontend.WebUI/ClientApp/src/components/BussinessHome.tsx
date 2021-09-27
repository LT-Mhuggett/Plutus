import React, { useState, useEffect, useCallback, useRef, Component } from 'react';

type Bussiness = {
    name: string,
    nameAbbr: string
};

const BussinessHome: React.FC<{ token: string }> = (props) => {
    const [bussiness, setBussiness] = useState([]);
    const [stores, setStores] = useState([]);

    const FetchBussiness: any = async (token: string) => {
        try {
            const response = await fetch('http://localhost:58559/api/Bussiness/6c3c802e-8658-4379-9307-4777a6bb5268', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzI3NjkyOTgsIm5iZiI6MTYzMjc2OTI5OCwiZXhwIjoxNjMyNzczMTk4LCJhaW8iOiJBWVFBZS84VEFBQUEvYjhjTTFmUGtkdy9icVA5eEd5dlNIR2dnc2xRLzVDZTNrUVpsVEUzN256b0I5RGY4Y3hTdG1lZW9hWDBNZEpPMVZvdllGMGZiWW40NXRiVjlJSm1hZXZEdS9DOVhoN0pRNEJPYzlkdzU5d2FtcTJLdEFQQmE1V1Q2UUExYmk3TjZoQzE2V0UrM2NkaExqUTgzWmlHQllYMkFxaVEvUjlMc1BhbzYzWmQ0RTQ9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6Ik5Fc0ltZjBIbmtDeGg4VEpMd09NQUEiLCJ2ZXIiOiIyLjAifQ.UUzRgyk96QLXVtY02O_U3xJdaQIYgwnAFawlI2taE0jNc-j_5LUCyShr7bHiWYg0_zsbqQAnapFg8ZNyv9B5URlqvcWMNFGQci2jkE_28N17WaUutDvTwC-siLGwIUtBRLBfH78dpABz2OALmFvssHYBTOt52Fq5AcCNKwQ6lbQXV3TcOCflbERSQYYvDJek3eZLzl6id_lW0x4Mj65ASiRJSaBUowQ2cGmApGL6jlcjuIbn_RgvnFFKVc-dmdr69RKgqo3JTS-4SUBLIa-7WwKffCK53GEqSv0_xSU8nw14cvC_mSCaDqqCy3f4cXhdV70YmYWEkKALOpEoLN9vOQ")
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });
            
            if (!response.ok) {
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
    // FetchBussiness(props.token);
    
    useEffect(() : any => {
        FetchBussiness(props.token);
    }, [])
    

    const FetchStores: any = async () => {
        try {
            const response = await fetch('http://localhost:58559/api/Store/Index', {
                method: 'GET',
                headers: {
                    'Content-Type': 'application/json',
                    'Authorization': 'Bearer '.concat("eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Imwzc1EtNTBjQ0g0eEJWWkxIVEd3blNSNzY4MCJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MzI3NjkyOTgsIm5iZiI6MTYzMjc2OTI5OCwiZXhwIjoxNjMyNzczMTk4LCJhaW8iOiJBWVFBZS84VEFBQUEvYjhjTTFmUGtkdy9icVA5eEd5dlNIR2dnc2xRLzVDZTNrUVpsVEUzN256b0I5RGY4Y3hTdG1lZW9hWDBNZEpPMVZvdllGMGZiWW40NXRiVjlJSm1hZXZEdS9DOVhoN0pRNEJPYzlkdzU5d2FtcTJLdEFQQmE1V1Q2UUExYmk3TjZoQzE2V0UrM2NkaExqUTgzWmlHQllYMkFxaVEvUjlMc1BhbzYzWmQ0RTQ9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6Ik5Fc0ltZjBIbmtDeGg4VEpMd09NQUEiLCJ2ZXIiOiIyLjAifQ.UUzRgyk96QLXVtY02O_U3xJdaQIYgwnAFawlI2taE0jNc-j_5LUCyShr7bHiWYg0_zsbqQAnapFg8ZNyv9B5URlqvcWMNFGQci2jkE_28N17WaUutDvTwC-siLGwIUtBRLBfH78dpABz2OALmFvssHYBTOt52Fq5AcCNKwQ6lbQXV3TcOCflbERSQYYvDJek3eZLzl6id_lW0x4Mj65ASiRJSaBUowQ2cGmApGL6jlcjuIbn_RgvnFFKVc-dmdr69RKgqo3JTS-4SUBLIa-7WwKffCK53GEqSv0_xSU8nw14cvC_mSCaDqqCy3f4cXhdV70YmYWEkKALOpEoLN9vOQ")
                    // 'Content-Type': 'application/x-www-form-urlencoded',
                },
            });

            if (!response.ok) {
                throw new Error('Something went wrong!');
            }

            const data = await response.json();

            setStores(data);
        } catch (error) {
            console.log("Something went wrong!");
            //setError(error.message);
        }
    }

    useEffect((): any => {
        FetchStores(props.token);
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