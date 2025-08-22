const express = require('express');
const { GoogleGenerativeAI } = require('@google/generative-ai');

const app = express();
app.use(express.json());

// IMPORTANT: Load your API key from environment variables, not hardcoded.
const genAI = new GoogleGenerativeAI(process.env.GEMINI_API_KEY);

app.post('/api/chat/stream', async (req, res) => {
  try {
    const { prompt, history } = req.body;

    // Set headers for Server-Sent Events (SSE)
    res.setHeader('Content-Type', 'text/event-stream');
    res.setHeader('Cache-Control', 'no-cache');
    res.setHeader('Connection', 'keep-alive');
    if (res.flushHeaders) res.flushHeaders();

    const model = genAI.getGenerativeModel({ model: 'gemini-pro' });

    const chat = model.startChat({ history });

    // This is the key function for streaming
    const result = await chat.sendMessageStream(prompt);

    // Iterate over the stream of chunks
    // chunk.text() returns the full accumulated response, so we need
    // to send only the new portion on each iteration to avoid
    // duplicating text in the client.
    let previousText = '';
    for await (const chunk of result.stream) {
      const chunkText = chunk.text();
      // Send only the delta since the last chunk
      const delta = chunkText.slice(previousText.length);
      if (delta) {
        res.write(`data: ${JSON.stringify({ text: delta })}\n\n`);
        previousText = chunkText;
      }
    }

    // When the stream is finished, close the connection
    res.end();

  } catch (error) {
    console.error('Error streaming response:', error);
    res.status(500).write('data: {"error": "An error occurred"}\n\n');
    res.end();
  }
});

const PORT = process.env.PORT || 3001;
app.listen(PORT, () => console.log(`Server running on port ${PORT}`));
