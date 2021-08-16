import http from 'k6/http';
import { check, group, sleep } from 'k6';

export let options = {
    insecureSkipTLSVerify: true,
    noConnectionReuse: false,
    stages: [
        { duration: '5m', target: 100 }, // simulate ramp-up of traffic from 1 to 100 users over 5 minutes.
        { duration: '10m', target: 100 }, // stay at 100 users for 10 minutes
        { duration: '5m', target: 0 }, // ramp-down to 0 users
    ],
    thresholds: {
        http_req_duration: ['p(99)<1500'], // 99% of requests must complete below 1.5s
        //'logged in successfully': ['p(99)<1500'], // 99% of requests must complete below 1.5s
    },
};

const BASE_URL = 'http://localhost:58559/api';

export default () => {
    let params = {
        cookies: { my_cookie: 'value' },
        headers: { 'Authorization': 'Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Im5PbzNaRHJPRFhFSzFqS1doWHNsSFJfS1hFZyJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MjkxMjEzNTcsIm5iZiI6MTYyOTEyMTM1NywiZXhwIjoxNjI5MTI1MjU3LCJhaW8iOiJBWVFBZS84VEFBQUFSRW8xcWxiRGhnOVY3TzJPS2xlK2hnbmpKZWJTQk9abXNKRWJtbERCTWd5dTc3c0NHNlUrM2U3cm01QTkxSXZPRUNKU2thbGQ1aVBMbmpRc05xS2lHdHMxYzNvUWpmbUJ4azhDdHJid09rWXZFMTVQQnVRY3h1ako1R0hyL095dEV6bkIyejB4WTVhRm4yNXhKSWQ3SVdnclJGcDFncmJMVFdLbnUzd1p3ZlE9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6Ikt4Y3RSalY4TjBhOXozZnBiaTB6QmciLCJ2ZXIiOiIyLjAifQ.XQ6gvFARe0IliSMCZwFv22abrPHpPFZEFvFKeHRdu85KA_1UhpU_wYbJDnb5FR0Y3d0baBtfRV3oTRWhx-hXDFSeTI9exhlUlQBGnqgjzVHBjL8PJww-f3HYkCxtlzdDTBOLK3WTJqOPsA3EptU7nQYKnPRlt_88fmWpXCC22aqT-f_7PUWhqGD0j9iaz0wKEksGE8woZsODoNn0t1U8oIfoBYIFN0DoIg7THQ3VAZD3FjohSWTsR_Y7JJKsyR3PbY_cMcjrNXECSCh9fOsjXRVmJc86Wzis9jmrZ8EijNIVY7gE1fDyNhHmDyv11M1-2PfSv1-L9--begM5ATZgDg' },
        redirects: 5,
        tags: { k6test: 'yes' },
    };

    http.get('http://localhost:58559/api/AuthAction/Index', params);
    http.get('http://localhost:58559/api/Bussiness/Index', params);
    http.get('http://localhost:58559/api/Category/Index', params);
    http.get('http://localhost:58559/api/Discount/Index', params);
    http.get('http://localhost:58559/api/Employee/Index', params);
    http.get('http://localhost:58559/api/Item/Index', params);
    http.get('http://localhost:58559/api/Note/Index', params);
    http.get('http://localhost:58559/api/PaymentMethod/Index', params);
    http.get('http://localhost:58559/api/Refund/Index', params);
    http.get('http://localhost:58559/api/Sale/Index', params);
    http.get('http://localhost:58559/api/SavedTransaction/Index', params);
    http.get('http://localhost:58559/api/Store/Index', params);
    http.get('http://localhost:58559/api/Stock/Index', params);
    http.get('http://localhost:58559/api/Tax/Index', params);
    http.get('http://localhost:58559/api/Till/Index', params);
    http.get('http://localhost:58559/api/Transaction/Index', params);
    sleep(1);
};
