import http from 'k6/http';
import { sleep } from 'k6';

export let options = {
    stages: [
        { duration: '10s', target: 100 }, // below normal load
        { duration: '1m', target: 100 },
        { duration: '10s', target: 1400 }, // spike to 1400 users
        { duration: '3m', target: 1400 }, // stay at 1400 for 3 minutes
        { duration: '10s', target: 100 }, // scale down. Recovery stage.
        { duration: '3m', target: 100 },
        { duration: '10s', target: 0 },
    ],
};
export default function () {
    const BASE_URL = 'http://localhost:58559/api'; // make sure this is not production

    let responses = http.batch([
        [
            'GET',
            `${BASE_URL}/AuthAction/Index/`,
            null,
            { tags: { name: 'AuthAction' } },
        ]
    ]);

    sleep(1);
}
