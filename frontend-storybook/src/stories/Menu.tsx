import './menu.css';

export interface MenuProps {
  items: string[];
}

export const Menu = ({ items }: MenuProps) => (
  <nav className="storybook-menu">
    <ul>
      {items.map((item) => (
        <li key={item}>
          <a href="#">{item}</a>
        </li>
      ))}
    </ul>
  </nav>
);

export default Menu;

