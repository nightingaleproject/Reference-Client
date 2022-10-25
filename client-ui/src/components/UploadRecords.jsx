import React, {useState} from 'react';
import { Container, Form, Button } from "react-bootstrap";

import 'bootstrap/dist/css/bootstrap.min.css';
import './UploadRecords.css'

function UploadRecords() {

  const [fileUploads, setFileUploads] = useState([]);

  const handleChange = (event) => {
    const files = [...event.target.files]; // Convert FileList to array
    setFileUploads(files.map(file => { return { file: file, done: false } }));
  };

  const handleUpload = (event) => {
    event.target.parentElement.reset(); // Reset the file upload form
    fileUploads.forEach((upload) => {
      const reader = new FileReader();
      reader.onload = (e) => {
        const contents = e.target.result.trim();
        // Basic check for JSON contents
        // TODO: Show error to user, and could also make sure it looks roughly like a record
        if (contents[0] !== '{') {
          console.log(`File ${upload.file.name} does not look like a FHIR VRDR record`);
        } else {
          fetch("/record/submission", {
            method: "POST",
            headers: { 'Content-Type': 'application/json' }, 
            body: contents
          }).then(response => {
            // Update the "done" state for the file that just got uploaded
            setFileUploads(fileUploads => {
              // TODO Show status to user on a per-file basis
              const updatedFileUploads = fileUploads.map(u => { return { ...u, done: u.done || u.file.name === upload.file.name } });
              // Once we've uploaded the last one we just reset the status and uploader
              if (updatedFileUploads.every(upload => upload.done)) {
                return [];
              } else {
                return updatedFileUploads;
              }
            });
            console.log(`Upload of ${upload.file.name} complete:`, response);
          });
        }
      };
      // TODO: Show error to user
      reader.onerror = (e) => console.error(`Error occured while reading ${upload.file.name}: `, e.target.error);
      reader.readAsText(upload.file);
    });
  }
  
  return(
    <Container fluid>
      <h2>Upload Records for Submission</h2>
      <Form id='file-upload-form'>
        <Form.Group controlId="formFileLg" className="mb-3">
          <Form.Control type="file" size="lg" multiple onChange={handleChange}/>
        </Form.Group>
        <h5>
          {fileUploads.length > 0 &&
           `${fileUploads.filter(status => status.done).length} / ${fileUploads.length} file${fileUploads.length !== 1 ? 's' : ''} uploaded`
          }
        </h5>
        <Button variant="primary" type="submit" disabled={fileUploads.length === 0} onClick={handleUpload}>
          Upload
        </Button>
      </Form>
    </Container>
  );
}

export default UploadRecords
