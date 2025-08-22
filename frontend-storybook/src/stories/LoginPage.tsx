import React from 'react';
import { Button } from './Button';
import './loginPage.css';

export const LoginPage: React.FC = () => {
  return (
    <div className="login-page">
      <div className="login-form">
        <h1>Login</h1>
        <input type="email" placeholder="Email" />
        <input type="password" placeholder="Password" />
        <Button label="Login" primary />
      </div>
    </div>
  );
};
