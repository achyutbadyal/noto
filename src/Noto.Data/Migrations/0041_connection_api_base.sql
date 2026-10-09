-- The API root for connections whose provider serves them from a different host than the site (Atlassian).
ALTER TABLE app_connection ADD COLUMN api_base_url TEXT;
