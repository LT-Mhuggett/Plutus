import { createSlice } from '@reduxjs/toolkit';

type AuthState = {
    isAuthenticated: boolean,
    token: string
};

const initialAuthState: AuthState = {
    isAuthenticated: false,
    token: ''
};

const authSlice = createSlice({
    name: 'authentication',
    initialState: initialAuthState,
    reducers: {
        login(state, action) {
            state.isAuthenticated = true;
            state.token = action.payload;
        },
        logout(state) {
            state.isAuthenticated = false;
        }
    }
});

export const authActions = authSlice.actions;

export default authSlice.reducer;