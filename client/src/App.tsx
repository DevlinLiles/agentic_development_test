import { Route, Routes } from "react-router-dom";
import { CreateGameScreen } from "./screens/CreateGameScreen";
import { GameScreen } from "./screens/GameScreen";

function App() {
  return (
    <Routes>
      <Route path="/" element={<CreateGameScreen />} />
      <Route path="/game/:gameId" element={<GameScreen />} />
    </Routes>
  );
}

export default App;
