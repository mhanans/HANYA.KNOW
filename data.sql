-- Seed data for shared enumerations

INSERT INTO assessment_step_definitions (status, step, step_name, description, job_status, job_outputs) VALUES
  ('Draft', 1, 'Draft assessment', 'Assessment data has been saved as a draft before automation begins.', NULL, ARRAY[]::text[]),
  ('Pending', 2, 'Inputs captured', 'Scope document and template references are stored and ready for AI generation.', 'Pending', ARRAY['scope_document_path', 'scope_document_mime_type', 'original_template_json', 'reference_assessments_json']),
  ('GenerationInProgress', 3, 'Generating assessment items', 'The automation is creating tailored assessment items from the provided inputs.', 'GenerationInProgress', ARRAY['raw_generation_response']),
  ('GenerationComplete', 4, 'Assessment items generated', 'Item generation finished successfully and serialized for later steps.', 'GenerationComplete', ARRAY['raw_generation_response', 'generated_items_json']),
  ('FailedGeneration', 5, 'Generation failed', 'Item generation encountered an error and can be resumed after repair.', 'FailedGeneration', ARRAY['last_error']),
  ('EstimationInProgress', 6, 'Estimating delivery effort', 'The automation is estimating delivery effort for the generated items.', 'EstimationInProgress', ARRAY['raw_estimation_response']),
  ('EstimationComplete', 7, 'Estimation finished', 'Effort estimation completed and analysis results are being stored.', 'EstimationComplete', ARRAY['raw_estimation_response', 'final_analysis_json']),
  ('FailedEstimation', 8, 'Estimation failed', 'Effort estimation encountered an error and can be resumed after repair.', 'FailedEstimation', ARRAY['last_error']),
  ('Complete', 9, 'Assessment complete', 'Assessment processing finished successfully and results are ready for review.', 'Complete', ARRAY['generated_items_json', 'final_analysis_json']),
  ('Completed', 10, 'Assessment archived', 'Assessment was completed and subsequently archived or confirmed.', 'Complete', ARRAY['generated_items_json', 'final_analysis_json'])
ON CONFLICT (status) DO UPDATE
SET step = EXCLUDED.step,
    step_name = EXCLUDED.step_name,
    description = EXCLUDED.description,
    job_status = EXCLUDED.job_status,
    job_outputs = EXCLUDED.job_outputs;
