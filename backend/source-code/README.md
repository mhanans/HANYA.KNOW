# Source Code Workspace

Place the repository or source files you want to expose to the Source Code Q&A feature inside this directory. The backend service automatically reads from this folder when you trigger a sync.

Run a sync from the Source Code Q&A page in the admin UI or by calling `POST /api/source-code/sync`. The job walks the directory tree, skips common build folders (`node_modules`, `.git`, `dist`, `bin`, `obj`, etc.), chunks files by line ranges, generates embeddings, and stores them in the `code_embeddings` table.

Use `GET /api/source-code/status` to check the last run, job duration, and number of processed files/chunks.
