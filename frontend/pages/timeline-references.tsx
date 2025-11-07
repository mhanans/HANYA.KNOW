import { FormEvent, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface TimelineReferenceResponse {
  id: number;
  projectScale: string;
  totalDurationDays: number;
  phaseDurations: Record<string, number>;
  resourceAllocation: Record<string, number>;
}

interface TimelineReference extends TimelineReferenceResponse {
  phaseDurationsText: string;
  resourceAllocationText: string;
}

interface TimelineReferenceDraft {
  projectScale: string;
  totalDurationDays: number;
  phaseDurationsText: string;
  resourceAllocationText: string;
}

const stringify = (value: Record<string, number>) => JSON.stringify(value, null, 2);

const emptyDraft: TimelineReferenceDraft = {
  projectScale: 'Medium',
  totalDurationDays: 30,
  phaseDurationsText: '{\n  "Discovery": 5,\n  "Development": 15,\n  "Testing": 10\n}',
  resourceAllocationText: '{\n  "Dev": 4,\n  "PM": 1,\n  "BA": 1\n}',
};

const parseObject = (text: string) => {
  const parsed = JSON.parse(text);
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('Value must be a JSON object.');
  }
  const result: Record<string, number> = {};
  for (const [key, raw] of Object.entries(parsed)) {
    const numberValue = Number(raw);
    if (!Number.isFinite(numberValue) || numberValue <= 0) {
      throw new Error(`Invalid numeric value for key "${key}".`);
    }
    result[key] = numberValue;
  }
  return result;
};

export default function TimelineReferencesPage() {
  const [references, setReferences] = useState<TimelineReference[]>([]);
  const [newReference, setNewReference] = useState<TimelineReferenceDraft>({ ...emptyDraft });
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const transform = (items: TimelineReferenceResponse[]) =>
    items.map(item => ({
      ...item,
      phaseDurationsText: stringify(item.phaseDurations),
      resourceAllocationText: stringify(item.resourceAllocation),
    }));

  const load = async () => {
    setLoading(true);
    try {
      const response = await apiFetch('/api/timeline-estimation-references');
      if (!response.ok) {
        throw new Error(await response.text());
      }
      const items: TimelineReferenceResponse[] = await response.json();
      setReferences(transform(items));
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    load();
  }, []);

  const resetNewReference = () => setNewReference({ ...emptyDraft });

  const handleSubmit = async (event: FormEvent) => {
    event.preventDefault();
    setError('');
    try {
      const phaseDurations = parseObject(newReference.phaseDurationsText);
      const resourceAllocation = parseObject(newReference.resourceAllocationText);
      const response = await apiFetch('/api/timeline-estimation-references', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          projectScale: newReference.projectScale,
          totalDurationDays: newReference.totalDurationDays,
          phaseDurations,
          resourceAllocation,
        }),
      });
      if (!response.ok) {
        throw new Error(await response.text());
      }
      resetNewReference();
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const updateReference = async (reference: TimelineReference) => {
    setError('');
    try {
      const phaseDurations = parseObject(reference.phaseDurationsText);
      const resourceAllocation = parseObject(reference.resourceAllocationText);
      const response = await apiFetch(`/api/timeline-estimation-references/${reference.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          projectScale: reference.projectScale,
          totalDurationDays: reference.totalDurationDays,
          phaseDurations,
          resourceAllocation,
        }),
      });
      if (!response.ok) {
        throw new Error(await response.text());
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  const deleteReference = async (id: number) => {
    setError('');
    try {
      const response = await apiFetch(`/api/timeline-estimation-references/${id}`, {
        method: 'DELETE',
      });
      if (!response.ok) {
        throw new Error(await response.text());
      }
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    }
  };

  return (
    <div className="page-container">
      <h1>Timeline Estimator References</h1>
      <p className="page-description">
        Configure historical project scales, phase durations, and headcount patterns that the AI timeline estimator should rely on before
        generating execution schedules.
      </p>

      <form className="card" onSubmit={handleSubmit} style={{ marginBottom: '24px' }}>
        <h2>Add New Reference</h2>
        <div className="form-grid">
          <label>
            <span>Project Scale</span>
            <input
              className="form-input"
              value={newReference.projectScale}
              onChange={event => setNewReference(prev => ({ ...prev, projectScale: event.target.value }))}
              placeholder="e.g., Short, Medium, Long"
              required
            />
          </label>
          <label>
            <span>Total Duration (Days)</span>
            <input
              className="form-input"
              type="number"
              min={1}
              value={newReference.totalDurationDays}
              onChange={event => setNewReference(prev => ({ ...prev, totalDurationDays: Number(event.target.value) || 0 }))}
              required
            />
          </label>
          <label style={{ gridColumn: '1 / span 2' }}>
            <span>Phase Durations (JSON Object)</span>
            <textarea
              className="form-input"
              rows={4}
              value={newReference.phaseDurationsText}
              onChange={event => setNewReference(prev => ({ ...prev, phaseDurationsText: event.target.value }))}
              placeholder='{"Discovery": 5, "Development": 15}'
              required
            />
          </label>
          <label style={{ gridColumn: '1 / span 2' }}>
            <span>Resource Allocation (JSON Object)</span>
            <textarea
              className="form-input"
              rows={4}
              value={newReference.resourceAllocationText}
              onChange={event => setNewReference(prev => ({ ...prev, resourceAllocationText: event.target.value }))}
              placeholder='{"Dev": 4, "PM": 1}'
              required
            />
          </label>
        </div>
        <div className="controls">
          <button type="submit" className="btn btn-primary">
            Add Reference
          </button>
          <button type="button" className="btn" onClick={resetNewReference}>
            Reset
          </button>
        </div>
      </form>

      <div className="card table-wrapper">
        <h2>Existing References</h2>
        {error && <p className="error-text">{error}</p>}
        {loading ? (
          <p>Loading reference data...</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Project Scale</th>
                <th>Total Duration (Days)</th>
                <th>Phase Durations (JSON)</th>
                <th>Resource Allocation (JSON)</th>
                <th style={{ width: '160px' }}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {references.length === 0 ? (
                <tr>
                  <td colSpan={5} style={{ textAlign: 'center' }}>
                    No reference data found.
                  </td>
                </tr>
              ) : (
                references.map(reference => (
                  <tr key={reference.id}>
                    <td>
                      <input
                        className="form-input"
                        value={reference.projectScale}
                        onChange={event =>
                          setReferences(prev =>
                            prev.map(ref =>
                              ref.id === reference.id
                                ? { ...ref, projectScale: event.target.value }
                                : ref
                            )
                          )
                        }
                        required
                      />
                    </td>
                    <td>
                      <input
                        className="form-input"
                        type="number"
                        min={1}
                        value={reference.totalDurationDays}
                        onChange={event =>
                          setReferences(prev =>
                            prev.map(ref =>
                              ref.id === reference.id
                                ? { ...ref, totalDurationDays: Number(event.target.value) || 0 }
                                : ref
                            )
                          )
                        }
                        required
                      />
                    </td>
                    <td>
                      <textarea
                        className="form-input"
                        rows={4}
                        value={reference.phaseDurationsText}
                        onChange={event =>
                          setReferences(prev =>
                            prev.map(ref =>
                              ref.id === reference.id
                                ? { ...ref, phaseDurationsText: event.target.value }
                                : ref
                            )
                          )
                        }
                        required
                      />
                    </td>
                    <td>
                      <textarea
                        className="form-input"
                        rows={4}
                        value={reference.resourceAllocationText}
                        onChange={event =>
                          setReferences(prev =>
                            prev.map(ref =>
                              ref.id === reference.id
                                ? { ...ref, resourceAllocationText: event.target.value }
                                : ref
                            )
                          )
                        }
                        required
                      />
                    </td>
                    <td className="table-actions">
                      <button type="button" className="btn btn-primary" onClick={() => updateReference(reference)}>
                        Save
                      </button>
                      <button type="button" className="btn btn-danger" onClick={() => deleteReference(reference.id)}>
                        Delete
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
