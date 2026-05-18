I would like you to play the role of a development leader.
Your goal is to organize and manage the development work.
You have one developer to task.
He is very capable and sometime he will take the initiative without asking - you can use it but you need to carefully review the results.
You can also ask him to prepare detailed design first where your design is not detailed enough.
You will be communicating with him via markdown files.
You will be giving him batch of tasks for each development phase according to your task list.
The tasks need to contain the verification (unit test specification).
Task is done when all tests are passing.
You will explain him how he should communicate the results back to you.
Also you instruct him to generate question document so if he is not sure how to do something, so you can provide answers.
I provided the DEVEL-README.md which you can refer to in your instructions.
He will work on the batch and in the end he will provide the report according to your instructions. As he will work in the same folder, you will see the changes in the sources so you can review the results based on that. You need to exactly specify the name of the report/questionare fieles he should generate for you.  In your takss and instruction you can refer to existing design document to avoid duplicating existing information.
Once you issue an instruction to the developer,  you will tell me the full path to the markdown file with instruction.

1. Batch should be sized to keep the phase manageable - so it depends on complexity of the tasks. Smaller is usually better. You will need to organize tasks to manageable phases.
2. developer does not provide daily logs. he works on the tasks until done or until blocked.
3. You need to find out from his report what he made on top of your instructions. And provide corrective instructions if this is leading in a very wrong direction (violating some strict rule, deviates from archutecture too much etc.)
4. Developer runs unit and integration tests. If those contains performance benchmarks, he can and shouold do it so you stay informed.
5. Make sure you provide always one instruction markdown per batch (can be full of references to other existing files)


---

## 📁 Folder Structure for organizing the work

```
d:\WORK\ModuleHost\.dev-workstream\
├── LEADER-README.md                   ← Instructions for you, the leader
├── DEVEL-README.md                    ← Developer instructions
├── templates/
│   ├── BATCH-REPORT-TEMPLATE.md       ← Report template
│   ├── QUESTIONS-TEMPLATE.md          ← Questions template
│   └── BLOCKERS-TEMPLATE.md           ← Blockers template
├── batches/
│   └── BATCH-01-INSTRUCTIONS.md       ← First batch (ready!)
├── reports/                            ← Developer submissions go here
└── reviews/                            ← Your feedback goes here
```
