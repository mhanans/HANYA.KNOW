import React, { useState } from 'react';
import { Button } from './Button';

export interface LoginPageProps {
  onSubmit: (data: { username: string; password: string }) => void;
}

export const LoginPage: React.FC<LoginPageProps> = ({ onSubmit }) => {
  const [username, setUsername] = useState('');
  const [password, setPassword] = useState('');
  return (
    <div className="login-page">
      <div className="login-form">
        <h1>Login</h1>
        <input
          type="text"
          placeholder="Username"
          value={username}
          onChange={e => setUsername(e.target.value)}
        />
        <input
          type="password"
          placeholder="Password"
          value={password}
          onChange={e => setPassword(e.target.value)}
        />
        <Button label="Login" primary onClick={() => onSubmit({ username, password })} />
      </div>
    </div>
  );
};
