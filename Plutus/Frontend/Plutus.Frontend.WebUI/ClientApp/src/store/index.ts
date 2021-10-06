import { configureStore } from '@reduxjs/toolkit';
import authReducer from './auth';

const authStore = configureStore({
    reducer: authReducer
});

export default authStore;