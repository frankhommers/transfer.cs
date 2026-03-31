import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { HomePage } from './pages/HomePage'
import { PreviewPage } from './pages/PreviewPage'

function App() {
  return (
    <BrowserRouter>
      <Routes>
        <Route path="/" element={<HomePage />} />
        <Route path="/:token/:filename" element={<PreviewPage />} />
      </Routes>
    </BrowserRouter>
  )
}

export default App
