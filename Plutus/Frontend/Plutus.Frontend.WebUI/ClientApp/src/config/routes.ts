import BussinessHome from '../components/BussinessHome';
import Store from '../components/Store';
import IRoute from '../interfaces/route';

const routes: IRoute[] = [
    {
        path: '/',
        name: 'Home Page',
        component: BussinessHome,
        exact: true
    },
    {
        path: '/store',
        name: 'Store Page',
        component: Store,
        exact: true
    }
]

export default routes;