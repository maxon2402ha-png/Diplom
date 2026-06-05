SELECT (SELECT COUNT(*) FROM "Ticket") AS tickets,
       (SELECT COUNT(*) FROM "KnowledgeArticle") AS kb,
       (SELECT COUNT(*) FROM "User") AS users;
