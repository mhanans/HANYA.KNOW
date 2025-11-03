import { FormEvent, useEffect, useState } from 'react';
import { apiFetch } from '../lib/api';

interface TimelineReference {
  id: number;
  phaseName: string;
  inputManHours: number;
  inputResourceCount: number;
  outputDurationDays: number;
}

interface TimelineReferenceDraft {
  phaseName: string;
  inputManHours: number;
  inputResourceCount: number;
  outputDurationDays: number;
}

const emptyDraft: TimelineReferenceDraft = {
  phaseName: '',
  inputManHours: 40,
  inputResourceCount: 1,
  outputDurationDays: 5,
};

export default function TimelineReferencesPage() {
  const [references, setReferences] = useState<TimelineReference[]>([]);
  const [newReference, setNewReference] = useState<TimelineReferenceDraft>({ ...emptyDraft });
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const load = async () => {
    setLoading(true);
    try {
      const response = await apiFetch('/api/timeline-estimation-references');
      if (!response.ok) {
        throw new Error(await response.text());
      }
      const items: TimelineReference[] = await response.json();
      setReferences(items);
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
      const response = await apiFetch('/api/timeline-estimation-references', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(newReference),
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
      const response = await apiFetch(`/api/timeline-estimation-references/${reference.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(reference),
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

  const handleNewNumberChange = (key: keyof TimelineReferenceDraft, value: string) => {
    const parsed = Number(value);
    setNewReference(prev => ({ ...prev, [key]: Number.isNaN(parsed) ? 0 : parsed }));
  };

  const handleExistingNumberChange = (id: number, key: keyof TimelineReference, value: string) => {
    const parsed = Number(value);
    setReferences(prev =>
      prev.map(ref =>
        ref.id === id
          ? {
              ...ref,
              [key]: Number.isNaN(parsed) ? 0 : parsed,
            }
          : ref
      )
    );
  };

  return (
    <div className="page-container">
      <h1>Timeline Duration Reference Table</h1>
      <p className="page-description">
        Maintain the historical rules that teach the AI how to transform man-hours and resource counts into realistic calendar
        durations for each phase.
      </p>

      <form className="card" onSubmit={handleSubmit} style={{ marginBottom: '24px' }}>
        <h2>Add New Reference</h2>
        <div className="form-grid">
          <label>
            <span>Phase Name</span>
            <input
              className="form-input"
              value={newReference.phaseName}
              onChange={event => setNewReference(prev => ({ ...prev, phaseName: event.target.value }))}
              placeholder="e.g., Application Development"
              required
            />
          </label>
          <label>
            <span>Total Man-Hours</span>
            <input
              className="form-input"
              type="number"
              min={1}
              value={newReference.inputManHours}
              onChange={event => handleNewNumberChange('inputManHours', event.target.value)}
              required
            />
          </label>
          <label>
            <span>Resource Count</span>
            <input
              className="form-input"
              type="number"
              min={1}
              value={newReference.inputResourceCount}
              onChange={event => handleNewNumberChange('inputResourceCount', event.target.value)}
              required
            />
          </label>
          <label>
            <span>Duration (Days)</span>
            <input
              className="form-input"
              type="number"
              min={1}
              value={newReference.outputDurationDays}
              onChange={event => handleNewNumberChange('outputDurationDays', event.target.value)}
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
        {loading ? (
          <p>Loading reference data...</p>
        ) : (
          <table className="table">
            <thead>
              <tr>
                <th>Phase</th>
                <th>Man-Hours</th>
                <th>Resources</th>
                <th>Duration (Days)</th>
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
                        value={reference.phaseName}
                        onChange={event =>
                          setReferences(prev =>
                            prev.map(ref => (ref.id === reference.id ? { ...ref, phaseName: event.target.value } : ref))
                          )
                        }
                      />
                    </td>
                    <td>
                      <input
                        className="form-input"
                        type="number"
                        min={1}
                        value={reference.inputManHours}
                        onChange={event => handleExistingNumberChange(reference.id, 'inputManHours', event.target.value)}
                      />
                    </td>
                    <td>
                      <input
                        className="form-input"
                        type="number"
                        min={1}
                        value={reference.inputResourceCount}
                        onChange={event => handleExistingNumberChange(reference.id, 'inputResourceCount', event.target.value)}
                      />
                    </td>
                    <td>
                      <input
                        className="form-input"
                        type="number"
                        min={1}
                        value={reference.outputDurationDays}
                        onChange={event => handleExistingNumberChange(reference.id, 'outputDurationDays', event.target.value)}
                      />
                    </td>
                    <td style={{ display: 'flex', gap: '8px' }}>
                      <button className="btn btn-primary" onClick={() => updateReference(reference)}>
                        Save
                      </button>
                      <button className="btn btn-danger" onClick={() => deleteReference(reference.id)}>
                        Delete
                      </button>
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        )}
        {error && <p className="error">{error}</p>}
      </div>
    </div>
  );
}
