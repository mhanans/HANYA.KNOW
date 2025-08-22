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

    // Iterate over the stream of chunks. The SDK's `chunk.text()`
    // sometimes returns the full accumulated text, while the
    // underlying parts contain only the latest delta. To avoid
    // duplicated output in either case, extract the text from the
    // parts and append only the new portion each iteration.
    let assembled = '';
    for await (const chunk of result.stream) {
      const parts = chunk.candidates?.[0]?.content?.parts ?? [];
      const chunkText = parts.map(p => p.text ?? '').join('');
      const delta = chunkText.startsWith(assembled)
        ? chunkText.slice(assembled.length)
        : chunkText;
      if (delta) {
        res.write(`data: ${JSON.stringify({ text: delta })}\n\n`);
        assembled += delta;
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
