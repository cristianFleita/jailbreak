import { ChakraProvider, defaultSystem } from '@chakra-ui/react'
import { GamePage } from './pages/GamePage'

function App() {
  return (
    <ChakraProvider value={defaultSystem}>
      <GamePage />
    </ChakraProvider>
  )
}

export default App