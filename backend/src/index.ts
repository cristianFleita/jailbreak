import express from 'express'
import { createServer } from 'http'
import { Server } from 'socket.io'
import cors from 'cors'
import dotenv from 'dotenv'
import { gameRouter } from './routes/game.js'
import { setupGameSockets } from './sockets/game.js'

dotenv.config()

const app = express()
const httpServer = createServer(app)

const io = new Server(httpServer, {
  cors: {
    origin: process.env.CLIENT_URL || 'http://localhost:5173',
    methods: ['GET', 'POST'],
  },
})

app.use(cors())
app.use(express.json())

app.use('/api/game', gameRouter)
app.get('/health', (_, res) => res.json({ status: 'ok' }))

setupGameSockets(io)

const PORT = process.env.PORT || 3001
httpServer.listen(PORT, () => {
  console.log(`🎮 Game server running on port ${PORT}`)
})