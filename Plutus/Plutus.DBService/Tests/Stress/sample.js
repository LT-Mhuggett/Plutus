import http from 'k6/http';
import { sleep } from 'k6';

export let options = {
    insecureSkipTLSVerify: true,
    noConnectionReuse: false,
    vus: 1,
    duration: '10s'
};

export default () => {

    let params = {
        cookies: { my_cookie: 'value' },
        headers: { 'Authorization': 'Bearer eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiIsImtpZCI6Im5PbzNaRHJPRFhFSzFqS1doWHNsSFJfS1hFZyJ9.eyJhdWQiOiIxMzFhYjM3Yi00MjUxLTRjMzctYjBjYy1jZTNhYWYzOTBkZTIiLCJpc3MiOiJodHRwczovL2xvZ2luLm1pY3Jvc29mdG9ubGluZS5jb20vZWQzOTgzMDAtOTIwZC00ZDM2LTljZGUtNWQzOTM3ZjE5YjdiL3YyLjAiLCJpYXQiOjE2Mjg3NzQwOTIsIm5iZiI6MTYyODc3NDA5MiwiZXhwIjoxNjI4Nzc3OTkyLCJhaW8iOiJBWVFBZS84VEFBQUEyd0Roc0I2Y3Jwb3cySVVQOFphczdIdjBPM2xTZHNrVGYvVTZ0RDNMYVl0cnR1V200aG5MSEUyY3crMG5nTm9jeks1dXMyTmF2RXNtd1UxU25IMWpkRFNJQjVyS1Urd1p3UUdNZjNraVEvSk9JUlhDTW5GbmVGajVJOVZpNC9SaytFbUE3R2hHM0MydEE1UkgrRlhuUWdOTDdYc09jZXkzekpUckhGZnZzbDQ9IiwiYXpwIjoiMTMxYWIzN2ItNDI1MS00YzM3LWIwY2MtY2UzYWFmMzkwZGUyIiwiYXpwYWNyIjoiMCIsImlkcCI6ImxpdmUuY29tIiwibmFtZSI6IlNpZGRoYXJ0aCBBZ3Jhd2FsIiwib2lkIjoiNGYyZWJhZTMtMmRjYy00ODQxLWEwOWUtMzIwOTAzZjJhODUzIiwicHJlZmVycmVkX3VzZXJuYW1lIjoic2lkZGhhcnRoYWdyYXdhbEBvdXRsb29rLmNvbSIsInJoIjoiMC5BU0lBQUlNNTdRMlNOazJjM2wwNU5fR2JlM3V6R2hOUlFqZE1zTXpPT3E4NURlSWtBRk0uIiwic2NwIjoiVGhpbmdzLlJlYWQgT3RoZXJUaGluZ3MuUmVhZCBQZXJtaXNzaW9uLldyaXRlIiwic3ViIjoia3hLOGtmUTBYSGJVZm1RRVBQLVNXckJEZDh1TkY1UmkxNE4wdC1hSWc0YyIsInRpZCI6ImVkMzk4MzAwLTkyMGQtNGQzNi05Y2RlLTVkMzkzN2YxOWI3YiIsInV0aSI6ImZIWjNuU1ZkSmthdGtPUkRDZW5BQkEiLCJ2ZXIiOiIyLjAifQ.OIFdL120yKAMPdj0ntqggg3HVTy2Maj1NCN9Au1nNyNXsqsYX3p3VzjS4FRdLni0w_fX4w-9AiS1IM59BuNqRY4fjQjpC4_x9g6S7hsL_t6iMi1gc5DI6p3LJl5VhHGZYrGBnC2a-GhVm1sZFZh-haiROuSvvoktNVTStxt4s8mQlOdhByvvBDsLaL0DNF61dcnLx-88ef0Wzxij_iG7z7dkTHEGGYuR_oFhDT6ieSAhOsBCJ3VOo33pK8t_NhXEvrj292m-5WzN_MD7bFASuEeccmeag1iYOIoMoTAKNObuEoQw0hlfaOarIIGAycbwM-jEjaHN121BnkqKLPWY6w' },
        redirects: 5,
        tags: { k6test: 'yes' },
    };

    http.get('http://localhost:58559/api/AuthAction/Index', params);
    //sleep(1);
};