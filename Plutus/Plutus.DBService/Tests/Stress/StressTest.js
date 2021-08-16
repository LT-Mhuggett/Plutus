import http from 'k6/http';
import { sleep } from 'k6';

export let options = {
    stages: [
        { duration: '2m', target: 100 }, // below normal load
        { duration: '5m', target: 100 },
        { duration: '2m', target: 200 }, // normal load
        { duration: '5m', target: 200 },
        { duration: '2m', target: 300 }, // around the breaking point
        { duration: '5m', target: 300 },
        { duration: '2m', target: 400 }, // beyond the breaking point
        { duration: '5m', target: 400 },
        { duration: '10m', target: 0 }, // scale down. Recovery stage.
    ],
};

export default function () {

    const BASE_URL = 'http://localhost:58559/api'; // make sure this is not production

    let apiToken = 'eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Im5PbzNaRHJPRFhFSzFqS1doWHNsSFJfS1hFZyJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MjkxMjEzNTcsIm5iZiI6MTYyOTEyMTM1NywiZXhwIjoxNjI5MTI1MjU3LCJhaW8iOiJBWVFBZS84VEFBQUFSRW8xcWxiRGhnOVY3TzJPS2xlK2hnbmpKZWJTQk9abXNKRWJtbERCTWd5dTc3c0NHNlUrM2U3cm01QTkxSXZPRUNKU2thbGQ1aVBMbmpRc05xS2lHdHMxYzNvUWpmbUJ4azhDdHJid09rWXZFMTVQQnVRY3h1ako1R0hyL095dEV6bkIyejB4WTVhRm4yNXhKSWQ3SVdnclJGcDFncmJMVFdLbnUzd1p3ZlE9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6Ikt4Y3RSalY4TjBhOXozZnBiaTB6QmciLCJ2ZXIiOiIyLjAifQ.XQ6gvFARe0IliSMCZwFv22abrPHpPFZEFvFKeHRdu85KA_1UhpU_wYbJDnb5FR0Y3d0baBtfRV3oTRWhx-hXDFSeTI9exhlUlQBGnqgjzVHBjL8PJww-f3HYkCxtlzdDTBOLK3WTJqOPsA3EptU7nQYKnPRlt_88fmWpXCC22aqT-f_7PUWhqGD0j9iaz0wKEksGE8woZsODoNn0t1U8oIfoBYIFN0DoIg7THQ3VAZD3FjohSWTsR_Y7JJKsyR3PbY_cMcjrNXECSCh9fOsjXRVmJc86Wzis9jmrZ8EijNIVY7gE1fDyNhHmDyv11M1-2PfSv1-L9--begM5ATZgDg';
    let params = {
        'User-Agent': 'k6',
        Authorization: 'Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Im5PbzNaRHJPRFhFSzFqS1doWHNsSFJfS1hFZyJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2MjkxMjQ0MzYsIm5iZiI6MTYyOTEyNDQzNiwiZXhwIjoxNjI5MTI4MzM2LCJhaW8iOiJBWVFBZS84VEFBQUFjOGdFVTNFSGorWHRZd0lVVW13YjczMHl5bFpKVG03R0Ewb0R3RHZPdy9oYnAxQ0NCWHNuSnpxUVZiTk9ZUmdJYVNsZ1lIQlR3Tmhod1FYZ3FzcEtMRkRFQy93T0paSTF3Mjl6Tm9DaUlhMmVXSEU4OUJxMUtqR1ZzYzBHcXcxMXp4NXUxZUhKcEJzRjJuWHp5Sm5ieUhyUlFDSncvTjVHY1ZIUjNtdXUvZ009IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6IjhiUU5hZ3hhdjBDbVZoeXhpcFh6QlEiLCJ2ZXIiOiIyLjAifQ.SLt_hIZXm4M-c9ik5xTTwpMJRRl3ZhMoxqsBo8qQn3yk-mpZg-dnMWqJ5jaPISWixKIJKpWg0IPVfPpiY1R_DMpApUgSETtdKZFVjI7eiDio3rY0h118V560GT3NSkmBuSjrnP7PA99qC-tCEz402eYrqCbCOiXthv3Ye-bA5xdbpQR0NcpGCKzX4ta5OPqdRfsdDETApXmFWUyR6lx2Nb07vVOd_X_G1rpwIwzf0RyCHs1Fkzsxo4QzdfB7-Or-7wG1R4mwyy9-qxf0-SiaGeLt-hfnBBkbHR8PaoCOE8T92qgFqR6slfAId9du5nL9PflhfKUkGQLTb9AJlBhF-w',
    };
    
    let responses = http.batch([
        [
            'GET',
            `${BASE_URL}/AuthAction/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Bussiness/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Category/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Discount/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Employee/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Item/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Note/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/PaymentMethod/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Refund/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Sale/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/SavedTransaction/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Store/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Stock/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Tax/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Till/Index/`,
            null,
            { headers: params },
        ],
        [
            'GET',
            `${BASE_URL}/Transaction/Index/`,
            null,
            { headers: params },
        ]
    ]);

    sleep(1);
}
