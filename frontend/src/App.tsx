import {BrowserRouter, Routes, Route} from 'react-router-dom'
import {HomePage} from './pages/HomePage'
import {PreviewPage} from './pages/PreviewPage'
import {ThemeToggle} from './components/ThemeToggle'
import {useTheme} from './hooks/useTheme'

function App() {
    const {mode, setMode} = useTheme()

    return (
        <BrowserRouter>
            <div className="fixed top-4 right-4 z-50">
                <ThemeToggle mode={mode} onChange={setMode}/>
            </div>
            <Routes>
                <Route path="/" element={<HomePage/>}/>
                <Route path="/:token/:filename" element={<PreviewPage/>}/>
            </Routes>
        </BrowserRouter>
    )
}

export default App
